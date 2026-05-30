namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

public static class CacheQueryBuilder_JoinOne_Extensions {

	// ════════════════════════════════════════════════════════════════════════════
	// Left-symmetric-index family (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Short-hand aliases used below (expand "NonExecBuilderR" as appropriate):
	//   NonExecBuilderR<TRightCache, TRightKey, TRightValue> ≡
	//     CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>,
	//       PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>,
	//       TRightKey, TRightValue,
	//       Resolvers<BaseResolver<TRightKey, TRightValue>>,
	//       TRightValue>
	//
	// Two base shapes × three filter variants = 6 overloads.

	// ── Shape A1: no rightIndex, no filter ───────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side — no filter.
	/// For each left entity, the index value (<typeparamref name="TRightKey"/>) is used
	/// as the primary key to look up the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape A2: no rightIndex, with filter ─────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side — with a filter on the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
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
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape A3: no rightIndex, with filter + arg ────────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side — with a filter + captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
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
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape B1: with rightIndex, no filter ─────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side, with an explicit unique right-side index — no filter.
	/// <paramref name="rightIndex"/> must be unique (1:1 semantics).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape B2: with rightIndex, with filter ───────────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side, with an explicit unique right-side index and a filter on the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape B3: with rightIndex, with filter + arg ─────────────────────────

	/// <summary>
	/// Join driven by a symmetric list index on the left side, with an explicit unique right-side index,
	/// a filter and a captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
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
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}
	// ════════════════════════════════════════════════════════════════════════════
	// Left-symmetric-index Inner family (6 overloads: 3 Shape A + 3 Shape B)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Lefts whose index value doesn't resolve to a right cache hit are dropped (Inner
	// semantic). Multiple lefts mapping to the same right key still fan out — FanOutContainer
	// ensures each one receives the right value when matched.

	// ── Inner A1: no rightIndex, no filter ─────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner A2: no rightIndex, with filter ───────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
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
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner A3: no rightIndex, with filter + arg ────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
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
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner B1: with rightIndex, no filter ───────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner B2: with rightIndex, with filter ─────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner B3: with rightIndex, with filter + arg ──────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
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
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// Convenience alias used in XML-doc; the full expansion appears in the where-constraint below.
	// NonExecBuilder = CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>,
	//                      PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>,
	//                      TKey, TRightValue,
	//                      Resolvers<BaseResolver<TKey, TRightValue>>,
	//                      TRightValue>

	/// <summary>
	/// Join one-to-one PK-to-PK with no filter.
	/// Pass the cache wrapper directly — no <c>.Query()</c> at the call site.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>(
			(TRightCache)rightCache, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// PK-to-PK Inner family (Indexed-Inner — drops lefts without right matches)
	// ════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Inner join PK-to-PK with no filter. Lefts without a matching right entity are
	/// <b>dropped</b> from the result (vs. outer which null-attaches them).
	/// Implementation: <c>PrepareIndexedInner</c> pre-narrows the outer left candidate
	/// set; the right-side attach runs only for survivors. One-pass, no post-filter.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>(
			(TRightCache)rightCache, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>
	/// Join one-to-one PK-to-PK with a lazily-applied filter on the right cache.
	/// Pass the cache wrapper directly — no <c>.Query()</c> at the call site.
	/// The filter callback receives a <see cref="NonExecutableQuery{TCache}"/>-discriminated
	/// builder so <c>WithXxx</c> extensions are available but <c>Execute*</c> is hidden.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>
	/// Join one-to-one PK-to-PK with a <typeparamref name="TArg"/> user-state parameter
	/// and a required filter that receives the arg as a second argument.
	/// Declare the lambda <c>static</c> to guarantee no closure allocation:
	/// <code>.JoinOne(_bCache, arg, static (q, a) => q.WithStatus(a))</code>
	/// Pass the cache wrapper directly — no <c>.Query()</c> at the call site.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner PK-to-PK with filter ──────────────────────────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a lazily-applied filter on the right cache. Lefts
	/// whose right entity fails the filter — or who have no right entity at all —
	/// are dropped from the result.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>(filter),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner PK-to-PK with filter + arg ────────────────────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a <typeparamref name="TArg"/> user-state parameter
	/// and a required filter that receives the arg as a second argument.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			IDataCache<TRightCache, TKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TKey, TRightValue, JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TKey, TRightValue>, TKey, TRightValue, Resolvers<BaseResolver<TKey, TRightValue>>, TRightValue>, TArg>, IdentitySelector<TKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// PK-to-PK + key selector family (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Call shape: .JoinOne(keySelector, [selectorArg,] rightCache, [filter, [filterArg]])
	//
	// keySelector maps TKey (left PK) to TRightKey (right PK). Permits PK-type
	// transformations between different key types (e.g. int → long, prefix → string).
	// Six variants: no-filter / filter / filter+arg, each × no-selectorArg / selectorArg.

	// ── S1: keySelector, no filter ───────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache, default, new KeySelector<TKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S2: keySelector + selectorArg, no filter ─────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache, default, new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S3: keySelector, with filter ─────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S4: keySelector + selectorArg, with filter ───────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S5: keySelector, with filter + filterArg ─────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S6: keySelector + selectorArg, with filter + filterArg ───────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// PK-to-PK + key selector Inner family (6 overloads — mirrors outer S1..S6)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Inner-join variants of the selector overloads above. Lefts whose mapped
	// right is missing (or filter-rejected) are dropped from the result. Each
	// mirrors its outer counterpart exactly and appends isInner: true to the
	// resolver ctor.

	// ── S1-Inner: keySelector, no filter ─────────────────────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector and no filter. Lefts whose mapped
	/// right is absent are dropped (vs. outer which null-attaches them).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache, default, new KeySelector<TKey, TRightKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S2-Inner: keySelector + selectorArg, no filter ───────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector that captures a <typeparamref name="TSelectorArg"/>
	/// (declare the lambda <c>static</c> for zero-alloc capture). Lefts whose mapped
	/// right is absent are dropped.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache, default, new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S3-Inner: keySelector, with filter ───────────────────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector and a lazily-applied filter on the
	/// right cache. Lefts whose mapped right is absent or filter-rejected are dropped.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TKey, TRightKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S4-Inner: keySelector + selectorArg, with filter ─────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector capturing a <typeparamref name="TSelectorArg"/>
	/// plus a filter on the right cache. Lefts whose mapped right is absent or
	/// filter-rejected are dropped.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S5-Inner: keySelector, with filter + filterArg ───────────────────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector and a filter taking a
	/// <typeparamref name="TFilterArg"/>. Lefts whose mapped right is absent or
	/// filter-rejected are dropped.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TKey, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TKey, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TKey, TRightKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TKey, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── S6-Inner: keySelector + selectorArg, with filter + filterArg ─────────

	/// <summary>
	/// Inner join PK-to-PK with a key selector capturing a <typeparamref name="TSelectorArg"/>
	/// and a filter taking a <typeparamref name="TFilterArg"/>. Lefts whose mapped
	/// right is absent or filter-rejected are dropped.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue,
		Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
		JoinResult<TValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TKey, TValue, TRightCache, TSelectorArg, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TKey, TValue, TResolverChain, TValue> builder,
			Func<TKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TKey, TValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>(
			(TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TKey, TSelectorArg, TRightKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>,
			JoinResult<TValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneResolver<TKey, TValue, TRightCache, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Right-unique-index family (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Call shape: .JoinOne(rightCache, rightUniqueIndex, [filter], [arg])
	//
	// rightUniqueIndex is CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey>
	// emitted by codegen from [DataCacheIndex(DataCacheIndexType.Unique)] on a
	// right-side FK property (e.g. BookInfo.BookId with Unique index).
	//
	// Per-leftKey execution:
	//   rightIndex.TryGetValue(leftKey, out rightKey) → candidate check → rightCache lookup → write to slot.
	//
	// Three filter variants: no-filter / filter / filter+TArg.

	// ── Shape R1: no filter ──────────────────────────────────────────────────

	/// <summary>
	/// Join driven by a unique index on the right side — no filter.
	/// For each left entity, <paramref name="rightIndex"/> maps the left primary key
	/// to the right primary key that holds the corresponding right value.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape R2: with filter ────────────────────────────────────────────────

	/// <summary>
	/// Join driven by a unique index on the right side — with a filter on the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape R3: with filter + arg ──────────────────────────────────────────

	/// <summary>
	/// Join driven by a unique index on the right side — with a filter and captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Right-unique-index Inner family (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(rightCache, rightUniqueIndex, [filter], [arg])
	// Lefts without a right match are dropped (Inner semantic). Implementation: paired-core
	// pre-narrow via _rightIndex.IntersectValues(candidates) — see resolver.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Left-unique-index family (3 overloads) — mirror of right-unique-index
	// ════════════════════════════════════════════════════════════════════════════
	//
	// Triggered by [DataCacheIndex(DataCacheIndexType.Unique, Symmetric = true)] TRightKey Foo
	// on the LEFT entity. Codegen emits the index as CacheSymmetricUniqueIndex whose .Reverse
	// gives the TLeftKey → TRightKey direction (1:1, bijective).

	// ── Shape L1: no filter ───────────────────────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric unique index on the left side — no filter.
	/// The left entity carries a unique FK pointing to a right entity; the index's
	/// reverse direction (<typeparamref name="TLeftKey"/> → <typeparamref name="TRightKey"/>)
	/// resolves which right entity to attach to each left.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape L2: with filter ────────────────────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric unique index on the left side — with a filter on the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Shape L3: with filter + arg ──────────────────────────────────────────

	/// <summary>
	/// Join driven by a symmetric unique index on the left side — with a filter and captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Left-unique-index Inner family (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(leftSymUniqueIndex, rightCache, [filter], [arg])
	// Lefts without a right match are dropped. Implementation: paired-core pre-narrow
	// via LeftIndex.Reverse.IntersectValues(candidates) — see resolver.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TRightKey> leftIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default,
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Sym-index Shape A + key selector (6 overloads): no rightIndex
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinOne(leftSymIndex, keySelector, [selectorArg,] rightCache, [filter, [filterArg]])
	// Selector maps the symIndex value (TLookupKey) → TRightKey directly.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, new KeySelector<TLookupKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLookupKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Sym-index Shape A + key selector — Inner (6 overloads): no rightIndex, drop-on-miss
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(leftSymIndex, keySelector, [selectorArg,] rightCache, [filter, [filterArg]])
	// Lefts whose mapped (TLookupKey → TRightKey) doesn't resolve to a right cache hit
	// — or whose right is filter-rejected — are dropped (Inner semantic). Multiple lefts
	// sharing the same lookup value still fan out via FanOutContainer; the whole fan-out
	// group is dropped when the mapped right is missing, while per-book filter rejection
	// drops only that left.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, new KeySelector<TLookupKey, TRightKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null, default, new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLookupKey, TRightKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TSelectorArg, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, null,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Sym-index Shape B + key selector (6 overloads): with rightIndex keyed by TRightIndexKey
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinOne(leftSymIndex, keySelector, [selectorArg,] rightCache, rightIndex, [filter, [filterArg]])
	// Selector maps TLookupKey → TRightIndexKey, then rightIndex translates to TRightKey.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default, new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default,
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Sym-index Shape B + key selector Inner family (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(leftSymIndex, keySelector, [selectorArg,] rightCache, rightIndex, [filter, [filterArg]])
	// Selector maps TLookupKey → TRightIndexKey, then rightIndex translates to TRightKey.
	// Lefts whose mapped right doesn't exist OR is rejected by the filter are dropped (Inner semantic).

	// ── Inner S1: selector, no filter ─────────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default, new KeySelector<TLookupKey, TRightIndexKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner S2: selector + selectorArg, no filter ──────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex, default,
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner S3: selector, with filter ──────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner S4: selector + selectorArg, with filter ───────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner S5: selector, with filter + filterArg ─────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ── Inner S6: selector + selectorArg, with filter + filterArg ───────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Right-unique-index + key selector (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinOne(keySelector, [selectorArg,] rightCache, rightIndex, [filter, [filterArg]])
	// Selector maps TLeftKey → TIndexKey, then rightIndex translates to TRightKey.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default, new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default,
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Right-unique-index + key selector Inner family (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(keySelector, [selectorArg,] rightCache, rightIndex, [filter, [filterArg]])
	// Inner semantic: lefts whose right entity is missing OR filter-rejected are dropped.
	// Selector maps TLeftKey → TIndexKey, then rightIndex translates to TRightKey.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default, new KeySelector<TLeftKey, TIndexKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default,
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLeftKey, TIndexKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLeftKey, TIndexKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneRightUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Left-unique-index + key selector (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinOne(leftSymUniqueIndex, keySelector, [selectorArg,] rightCache, [filter, [filterArg]])
	// Selector maps TIndexKey (leftIndex's lookup value) → TRightKey.

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, default, new KeySelector<TIndexKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, default,
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TIndexKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TIndexKey, TRightKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		JoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Left-unique-index + key selector Inner family (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinOne(leftSymUniqueIndex, keySelector, [selectorArg,] rightCache, [filter, [filterArg]])
	// Inner-join variant: lefts whose mapped right key is missing OR whose right
	// row is filtered out are DROPPED from the result. Selector maps
	// TIndexKey (leftIndex's lookup value via Reverse) → TRightKey.

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector. Lefts without a matching right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache, default, new KeySelector<TIndexKey, TRightKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector with a captured selector argument. Lefts without a matching right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache, default,
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector with a right-side filter. Lefts without a matching (and filter-passing) right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TIndexKey, TRightKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector with a captured selector argument and a right-side filter. Lefts without a matching (and filter-passing) right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector with a right-side filter and captured filter argument. Lefts without a matching (and filter-passing) right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TIndexKey, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TRightKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TIndexKey, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TIndexKey, TRightKey>(keySelector),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TIndexKey, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TIndexKey, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}

	/// <summary>Inner level-0 join using a left-symmetric-unique index + key selector with captured selector and filter arguments. Lefts without a matching (and filter-passing) right are dropped.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
		JoinResult<TLeftValue, TRightValue?>>
		InnerJoinOne<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TSelectorArg, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricUniqueIndex<TLeftKey, TLeftValue, TIndexKey> leftIndex,
			Func<TIndexKey, TSelectorArg, TRightKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg filterArg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>(
			leftIndex, (TRightCache)rightCache,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>(keySelector, selectorArg),
			isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>,
			JoinResult<TLeftValue, TRightValue?>>(
			new Resolvers<TResolverChain, JoinOneLeftUniqueIndexResolver<TLeftKey, TLeftValue, TRightCache, TIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TIndexKey, TSelectorArg, TRightKey>>>(
				builder._resolverChain, resolver),
			false);
	}
}

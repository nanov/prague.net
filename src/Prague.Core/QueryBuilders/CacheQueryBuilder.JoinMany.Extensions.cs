namespace Prague.Core;

using System.Runtime.CompilerServices;
using Collections;
using TypeSystem;

public static class CacheQueryBuilder_JoinMany_Extensions {

	// ════════════════════════════════════════════════════════════════════════════
	// Right-list-index family — identity selector (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(rightCache, rightIndex, [filter, [arg]])
	// Driven by a non-unique list index on the right side. For each left entity,
	// rightIndex maps the left primary key to the set of matching right primary keys.

	// ── Shape M1: identity, no filter ────────────────────────────────────────

	/// <summary>
	/// Join driven by a list index on the right side — no filter.
	/// For each left entity, <paramref name="rightIndex"/> maps the left primary key
	/// to the set of right primary keys that reference it.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape M2: identity, with filter ──────────────────────────────────────

	/// <summary>
	/// Join driven by a list index on the right side — with a filter on the right cache.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape M3: identity, with filter + arg ────────────────────────────────

	/// <summary>
	/// Join driven by a list index on the right side — with a filter and captured arg (zero-alloc with static lambda).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Right-list-index + key selector (6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(keySelector, [selectorArg,] rightCache, rightIndex, [filter, [filterArg]])
	// Selector maps TLeftKey → TIndexKey, then rightIndex translates to TRightKey.

	// ── Shape MS1: KeySelector, no filter ────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default, new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MS2: KeySelector, with filter ──────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MS3: KeySelector, with filter + filterArg ──────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLeftKey, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelector<TLeftKey, TIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLeftKey, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MSA1: KeySelectorWithArg, no filter ────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TIndexKey : notnull, IEquatable<TIndexKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex, default,
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MSA2: KeySelectorWithArg, with filter ──────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MSA3: KeySelectorWithArg, with filter + filterArg ──────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TSelectorArg, TIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			Func<TLeftKey, TSelectorArg, TIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TIndexKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, filterArg),
			new KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TIndexKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLeftKey, TSelectorArg, TIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// InnerJoinMany — identity selector (3 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .InnerJoinMany(rightCache, rightIndex, [filter, [arg]])
	// Lefts whose per-left QueryResults<TRightValue> is empty (no rights matched OR
	// filter rejected them all) are dropped. Implementation: paired-core keyed-init
	// flow + post-walk accessor.RetainNonEmptyManySlots — see resolver.

	// ── Shape MI1: identity, no filter ───────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MI2: identity, with filter ─────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// JoinMany LeftSym — identity selector (3 outer + 3 inner = 6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(leftSymIndex, rightCache, rightIndex, [filter, [arg]])
	// Left has CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey>.
	// For each leftKey: Reverse → lookupKey → right's CacheKeyValueListIndex bucket.
	// Identity: TLookupKey == TRightIndexKey.

	// ── Shape LS1: identity, no filter ───────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LS2: identity, with filter ─────────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LS3: identity, with filter + arg ───────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
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
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSI1: inner, identity, no filter ───────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSI2: inner, identity, with filter ─────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSI3: inner, identity, with filter + arg ───────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLookupKey> rightIndex,
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
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLookupKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TLookupKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLookupKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// JoinMany LeftSym — Func key selector (3 outer + 3 inner = 6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(leftSymIndex, keySelector, rightCache, rightIndex, [filter, [arg]])
	// Selector maps TLookupKey → TRightIndexKey at left-iteration time, so the
	// right list index may be keyed by a DIFFERENT key type than the left
	// symmetric index.

	// ── Shape LSS1: Func selector, no filter ─────────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSS2: Func selector, with filter ───────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSS3: Func selector, with filter + arg ─────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, arg),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSI1: inner, Func selector, no filter ─────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, new KeySelector<TLookupKey, TRightIndexKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSI2: inner, Func selector, with filter ───────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSI3: inner, Func selector, with filter + arg ─────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TRightIndexKey> keySelector,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelector<TLookupKey, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, arg),
			new KeySelector<TLookupKey, TRightIndexKey>(keySelector), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelector<TLookupKey, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// JoinMany LeftSym — Func+arg key selector (3 outer + 3 inner = 6 overloads)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(leftSymIndex, keySelector, selectorArg, rightCache, rightIndex, [filter, [arg]])
	// Selector form Func<TLookupKey, TSelectorArg, TRightIndexKey> + a TSelectorArg
	// for zero-alloc static-lambda capture.

	// ── Shape LSSA1: Func+arg selector, no filter ────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSA2: Func+arg selector, with filter ──────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSA3: Func+arg selector, with filter + arg ────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, arg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg));
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSAI1: inner, Func+arg selector, no filter ────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex, default, new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				NoFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSAI2: inner, Func+arg selector, with filter ──────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>(filter),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilter<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape LSSAI3: inner, Func+arg selector, with filter + arg ────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TLookupKey, TSelectorArg, TRightIndexKey, TRightCache, TRightKey, TFilterArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			CacheSymmetricKeyValueListIndex<TLeftKey, TLeftValue, TLookupKey> leftSymIndex,
			Func<TLookupKey, TSelectorArg, TRightIndexKey> keySelector,
			TSelectorArg selectorArg,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TRightIndexKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				TFilterArg,
				CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> filter,
			TFilterArg arg)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TLookupKey : notnull, IEquatable<TLookupKey>
		where TRightIndexKey : notnull, IEquatable<TRightIndexKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue> {
		var resolver = new JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
			KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>(
			leftSymIndex, (TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>(filter, arg),
			new KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>(keySelector, selectorArg), isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyLeftSymResolver<TLeftKey, TLeftValue, TRightCache, TLookupKey, TRightIndexKey, TRightKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<LeftKeySetView<TLeftKey>, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TFilterArg>,
				KeySelectorWithArg<TLookupKey, TSelectorArg, TRightIndexKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ── Shape MI3: identity, with filter + arg ───────────────────────────────

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>>,
		JoinResult<TLeftValue, QueryResults<TRightValue>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TArg, TRightValue>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
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
		var resolver = new JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
			JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
			IdentitySelector<TLeftKey>>(
			(TRightCache)rightCache, rightIndex,
			new JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>(filter, arg),
			default, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>,
			JoinResult<TLeftValue, QueryResults<TRightValue>>>(
			new Resolvers<TResolverChain, JoinManyRightListIndexResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TLeftKey, TRightValue,
				JoinFilterWithArg<CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>, TArg>,
				IdentitySelector<TLeftKey>>>(
				builder._resolverChain, resolver),
			true);
	}

	// ════════════════════════════════════════════════════════════════════════════
	// Nested family — identity selector, no outer filter (v1)
	// ════════════════════════════════════════════════════════════════════════════
	//
	// .JoinMany(rightCache, rightIndex, innerBuilder => innerBuilder.JoinXxx(...))
	// True nesting: each parent row carries QueryResults<JoinResult<Child, ...>>, the inner
	// join scoped to each Child. The continuation receives a fresh sub-query-builder rooted at
	// the child cache and is invoked ONCE at build time to capture the inner plan into the type.

	/// <summary>
	/// Nested JoinMany — the Many-side child is itself joined via the <paramref name="nested"/>
	/// continuation. Result shape: <c>JoinResult&lt;TLeftValue, QueryResults&lt;TInnerResult&gt;&gt;</c>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>,
		JoinResult<TLeftValue, QueryResults<TInnerResult>>>
		JoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<ExecutableQuery<TRightCache>, CacheQueryBuilderCoreCombined<TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<TInnerDisc, TInnerExec, TRightKey, TRightValue, TInnerChain, TInnerResult>> nested)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
		where TInnerDisc : struct
		where TInnerExec : struct, ICandidatesExecutor<TRightKey, TRightValue>
		where TInnerChain : struct, IResolvers
		where TInnerResult : struct, IJoinResult<TRightValue> {
		var typedCache = (TRightCache)rightCache;
		// Invoke the continuation ONCE at build time to capture the inner plan into the type. The seed
		// is the right cache's OWN Query() (discriminator ExecutableQuery<TRightCache>) so codegen FK
		// wrappers compose their JoinWith sugar inside the continuation.
		var innerBuilder = nested(rightCache.Query());
		var resolver = new JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>(
			typedCache, rightIndex, innerBuilder);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>,
			JoinResult<TLeftValue, QueryResults<TInnerResult>>>(
			new Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>(
				builder._resolverChain, resolver),
			true);
	}

	/// <summary>
	/// Nested InnerJoinMany — like the nested <see cref="JoinMany"/> but drops parents whose inner
	/// collection is empty (no children, or all children dropped by the inner join/filter). Same
	/// drop-empty-parent semantics as the flat <c>InnerJoinMany</c> (via <c>isInner: true</c> →
	/// post-walk <c>RetainNonEmptyManySlots</c>).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue,
		Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>,
		JoinResult<TLeftValue, QueryResults<TInnerResult>>>
		InnerJoinMany<TDiscriminator, TExecutor, TResolverChain, TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>(
			this in CacheQueryBuilderCombined<TDiscriminator, TExecutor, TLeftKey, TLeftValue, TResolverChain, TLeftValue> builder,
			IDataCache<TRightCache, TRightKey, TRightValue> rightCache,
			CacheKeyValueListIndex<TRightKey, TRightValue, TLeftKey> rightIndex,
			Func<
				CacheQueryBuilderCombined<ExecutableQuery<TRightCache>, CacheQueryBuilderCoreCombined<TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>,
				CacheQueryBuilderCombined<TInnerDisc, TInnerExec, TRightKey, TRightValue, TInnerChain, TInnerResult>> nested)
		where TExecutor : struct, ICandidatesExecutor<TLeftKey, TLeftValue>
		where TDiscriminator : struct, IBaseJoinable
		where TResolverChain : struct, IResolvers
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TLeftValue : ICacheEquatable<TLeftValue>, ICacheClonable<TLeftValue>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
		where TInnerDisc : struct
		where TInnerExec : struct, ICandidatesExecutor<TRightKey, TRightValue>
		where TInnerChain : struct, IResolvers
		where TInnerResult : struct, IJoinResult<TRightValue> {
		var typedCache = (TRightCache)rightCache;
		var innerBuilder = nested(rightCache.Query());
		var resolver = new JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>(
			typedCache, rightIndex, innerBuilder, isInner: true);
		return Unsafe.AsRef(in builder).AddResolver<
			Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>,
			JoinResult<TLeftValue, QueryResults<TInnerResult>>>(
			new Resolvers<TResolverChain, JoinManyNestedResolver<TLeftKey, TLeftValue, TRightCache, TRightKey, TRightValue, TInnerDisc, TInnerExec, TInnerChain, TInnerResult>>(
				builder._resolverChain, resolver),
			true);
	}
}

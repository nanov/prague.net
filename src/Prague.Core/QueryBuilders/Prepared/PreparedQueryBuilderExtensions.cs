namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>
///   Entry points and narrowing extensions of the prepared builder. Each narrowing call returns a
///   builder whose left query carries one more <see cref="NarrowerLink{TPrev,TNarrower,TKey,TValue,TArgs}" />;
///   nothing touches the cache until <c>Build()</c> and then <c>Execute</c>. The builder type itself
///   is the ordinary <see cref="CacheQueryBuilderCombined{TDiscriminator,TLeftQuery,TLeftKey,TLeftValue,TResolverChain,TResult}" />,
///   constructed here rather than modified, so joins reuse as-is. The narrowing overloads are generic
///   over the discriminator (<see cref="IIndexNarrower" />, as the eager ones) so they bind both on the
///   top-level <see cref="PreparedQueryDiscriminator{TCache}" /> and inside an <c>Or</c> branch
///   (<see cref="PreparedNarrowOnly{TCache}" />); <c>Where</c> keeps the eager <see cref="IBaseFilterable" />
///   gate, so a branch cannot filter.
/// </summary>
public static class PreparedQueryBuilderExtensions {
	/// <summary>Starts a prepared query whose values are all bound at build time.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, NoArgs, EmptyNarrowers<TKey, TValue, NoArgs>>, TKey, TValue,
			Resolvers<BaseResolver<TKey, TValue>>, TValue>
		Prepare<TKey, TValue>(this InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> cache.Prepare<InMemoryDataCache<TKey, TValue>, TKey, TValue, NoArgs>(cache);

	/// <summary>Starts a prepared query parameterized by <typeparamref name="TArgs" />, supplied on every execution.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue,
			Resolvers<BaseResolver<TKey, TValue>>, TValue>
		Prepare<TKey, TValue, TArgs>(this InMemoryDataCache<TKey, TValue> cache)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> cache.Prepare<InMemoryDataCache<TKey, TValue>, TKey, TValue, TArgs>(cache);

	/// <summary>
	///   Starts a prepared query over <paramref name="cache" /> whose discriminator carries
	///   <paramref name="carrier" /> instead of the raw cache. This is the entry point the generated
	///   <c>XxxCache.Prepare()</c> / <c>Prepare&lt;TArgs&gt;()</c> use, passing the wrapper as the carrier:
	///   the generated <c>WithXxx</c> / <c>JoinWith{T}</c> extensions bind on
	///   <see cref="ICacheCarrier{TCache}" /> of the wrapper type, exactly as the eager ones bind on
	///   <see cref="ExecutableQuery{TCache}" />, which scopes them to that one cache. <c>Or</c> and <c>If</c>
	///   branch builders inherit the same <typeparamref name="TCache" /> and carrier value, so the
	///   generated narrowing extensions bind inside branches too. The raw-cache overloads pass the cache
	///   itself as its own carrier.
	/// </summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>, TKey, TValue,
			Resolvers<BaseResolver<TKey, TValue>>, TValue>
		Prepare<TCache, TKey, TValue, TArgs>(this InMemoryDataCache<TKey, TValue> cache, TCache carrier)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		=> new(new PreparedQueryDiscriminator<TCache>(carrier),
			new PreparedNarrowers<TKey, TValue, TArgs, EmptyNarrowers<TKey, TValue, TArgs>>(cache, default),
			new Resolvers<BaseResolver<TKey, TValue>>(new BaseResolver<TKey, TValue>()),
			0);

	// ── Narrowing ────────────────────────────────────────────────────────────────

	// Internal (not private) so the sibling range / last-updated extension classes append links the same way.
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, TNarrower, TKey, TValue, TArgs>>, TKey, TValue,
			TResolverChain, TResult>
		Link<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TNarrower>(
			in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			in TNarrower narrower)
		where TDiscriminator : struct
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TNarrower : struct, INarrower<TKey, TValue, TArgs> {
		ref readonly var left = ref builder._leftQuery;
		return new(builder._discriminator,
			new PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, TNarrower, TKey, TValue, TArgs>>(
				left._cache, new NarrowerLink<TChain, TNarrower, TKey, TValue, TArgs>(in left._chain, in narrower)),
			builder._resolverChain,
			builder._manyCount);
	}

	/// <summary>Unique index equality, value bound now.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, UniqueIndexEq<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			TIndexKey value)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new UniqueIndexEq<TKey, TValue, TIndexKey, TArgs>(index, value));

	/// <summary>Unique index equality, value selected from the execution arguments (use a static lambda).</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, UniqueIndexEqArg<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, TIndexKey> selector)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new UniqueIndexEqArg<TKey, TValue, TIndexKey, TArgs>(index, selector));

	/// <summary>List index equality, value bound now.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexEq<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			TIndexKey value)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexEq<TKey, TValue, TIndexKey, TArgs>(index, value));

	/// <summary>List index equality, value selected from the execution arguments (use a static lambda).</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexEqArg<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, TIndexKey> selector)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexEqArg<TKey, TValue, TIndexKey, TArgs>(index, selector));

	/// <summary>Value predicate bound now. Chained calls are ANDed in order, as in the eager builder.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, FilterNarrower<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Where<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			Predicate<TValue> predicate)
		where TDiscriminator : struct, IBaseFilterable
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> Link(in builder, new FilterNarrower<TKey, TValue, TArgs>(predicate));

	/// <summary>
	///   Value predicate over the row and the execution arguments (use a static lambda), ANDed in order
	///   with the other filters. Zero-allocation per execution: <c>args</c> is bound into a per-thread
	///   pooled predicate box, never a closure. Overload resolution against the bound <c>Where</c> is by
	///   lambda arity: <c>v =&gt; …</c> binds <see cref="Predicate{T}" />, <c>(v, a) =&gt; …</c> binds this.
	/// </summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, FilterArgNarrower<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		Where<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			ArgFilter<TValue, TArgs> predicate)
		where TDiscriminator : struct, IBaseFilterable
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> Link(in builder, new FilterArgNarrower<TKey, TValue, TArgs>(predicate));

	// ── Multi-value ──────────────────────────────────────────────────────────────
	//
	// Overload resolution note. The multi-value overloads share the name UseIndex with the
	// single-value ones on purpose (matching the eager builder, which pairs TIndexKey with
	// ReadOnlySpan<TIndexKey>). They cannot collide: TIndexKey is pinned by the `index` argument, so
	// `UseIndex(index, 12)` binds only the TIndexKey overload (12 is neither an array nor a memory),
	// and `UseIndex(index, new[] { 1, 2 })` binds only the TIndexKey[] overload (an int[] is not an int,
	// and the identity conversion beats the user-defined conversion to ReadOnlyMemory<int>). The array
	// overload exists so callers do not have to spell `.AsMemory()`; both feed the same narrower.

	/// <summary>Unique index membership in a value set bound now. Empty set yields no rows, as in the eager span overload.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, UniqueIndexIn<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			ReadOnlyMemory<TIndexKey> values)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new UniqueIndexIn<TKey, TValue, TIndexKey, TArgs>(index, values));

	/// <summary>Unique index membership in an array of values bound now.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, UniqueIndexIn<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			TIndexKey[] values)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new UniqueIndexIn<TKey, TValue, TIndexKey, TArgs>(index, values));

	/// <summary>Unique index membership in a value set selected from the execution arguments (use a static lambda).</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, UniqueIndexInArg<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, ReadOnlyMemory<TIndexKey>> selector)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new UniqueIndexInArg<TKey, TValue, TIndexKey, TArgs>(index, selector));

	/// <summary>List index membership in a value set bound now. Empty set yields no rows, as in the eager span overload.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexIn<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			ReadOnlyMemory<TIndexKey> values)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexIn<TKey, TValue, TIndexKey, TArgs>(index, values));

	/// <summary>List index membership in an array of values bound now.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexIn<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			TIndexKey[] values)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexIn<TKey, TValue, TIndexKey, TArgs>(index, values));

	/// <summary>List index membership in a value set selected from the execution arguments (use a static lambda).</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexInArg<TKey, TValue, TIndexKey, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			Func<TArgs, ReadOnlyMemory<TIndexKey>> selector)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexInArg<TKey, TValue, TIndexKey, TArgs>(index, selector));

	/// <summary>List index membership over bound foreign values, each projected to an index key by <paramref name="keySelector" /> at replay.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, ListIndexInProjected<TKey, TValue, TIndexKey, TOtherValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult, TIndexKey, TOtherValue>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeyValueListIndex<TKey, TValue, TIndexKey> index,
			ReadOnlyMemory<TOtherValue> values,
			Func<TOtherValue, TIndexKey> keySelector)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TIndexKey : notnull
		=> Link(in builder, new ListIndexInProjected<TKey, TValue, TIndexKey, TOtherValue, TArgs>(index, values, keySelector));

	/// <summary>Key-set (predicate) index.</summary>
	public static CacheQueryBuilderCombined<TDiscriminator,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, KeySetNarrower<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TDiscriminator, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<TDiscriminator,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			CacheKeySetIndex<TKey, TValue> index)
		where TDiscriminator : struct, IIndexNarrower
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> Link(in builder, new KeySetNarrower<TKey, TValue, TArgs>(index));

	// ── Terminal ─────────────────────────────────────────────────────────────────

	/// <summary>
	///   Freezes the description into a reusable command. The only allocation of a prepared query
	///   happens here. Simple (no-join) shape: <c>TResult</c> is the cache value. The terminals are
	///   generic over the carrier type: they read the cache from the recorder, never from the discriminator.
	/// </summary>
	public static PreparedQuery<TArgs, TValue> Build<TCache, TKey, TValue, TArgs, TChain, TResolver>(
		this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<TResolver>, TValue> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		=> new PreparedSimpleQuery<TKey, TValue, TArgs, TChain, TResolver, ClassicSimplePlan>(builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain);

	/// <summary>
	///   Freezes a joined description into a reusable command. Executions route through the eager
	///   joined terminals' core (<c>ExecuteCoreJoined</c> / <c>CountCoreJoined</c>) over the replayed
	///   left query, so inner joins, chained joins and 1:N fan-out behave exactly as eager.
	/// </summary>
	/// <remarks>
	///   Overload selection against the simple <c>Build()</c>: that one pins the result to the cache
	///   value and the chain to a single <c>Resolvers&lt;TResolver&gt;</c>; this one requires
	///   <typeparamref name="TResult" /> to be a <c>struct</c> join result over the value. A cache value
	///   cannot satisfy both, so exactly one applies to any builder.
	///   <para>
	///   The right-side join filters (<c>Func&lt;TBuilder, TBuilder&gt;</c> and the
	///   <c>Func&lt;TBuilder, TArg, TBuilder&gt;</c> + <c>arg</c> overloads) already run at execute time
	///   and need no recording, but they are bound at build time: a filter cannot read
	///   <typeparamref name="TArgs" />. Parameterizing the right side of a join is not part of this
	///   step.
	///   </para>
	/// </remarks>
	public static PreparedQuery<TArgs, TResult> Build<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> new PreparedJoinedQuery<TKey, TValue, TArgs, TChain, TResolverChain, TResult, ClassicJoinedPlan>(
			builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, builder._manyCount);

	// ── Frozen terminal ──────────────────────────────────────────────────────────

	/// <summary>
	///   <see cref="Build{TCache,TKey,TValue,TArgs,TChain,TResolver}" />, plus a build-time plan: the
	///   chain is flattened into <see cref="PlanInfo" /> and, when it is a unique-index lookup followed
	///   only by filters, bound to the point-lookup executor; every other shape replays exactly as
	///   <c>Build()</c>. Results are identical either way; <see cref="FrozenQuery{TArgs,TResult}.Explain" />
	///   says which executor was chosen.
	/// </summary>
	public static FrozenQuery<TArgs, TValue> BuildFrozen<TCache, TKey, TValue, TArgs, TChain, TResolver>(
		this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, Resolvers<TResolver>, TValue> builder, FrozenOptions? options = null)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolver : struct, IJoinResolver
		=> FrozenPlanner.Simple<TKey, TValue, TArgs, TChain, TResolver, ClassicSimplePlan>(builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, false, options ?? FrozenOptions.Default);

	/// <summary>The joined <see cref="Build{TCache,TKey,TValue,TArgs,TChain,TResolverChain,TResult}" /> with plan metadata; joined shapes always replay in stage 1.</summary>
	public static FrozenQuery<TArgs, TResult> BuildFrozen<TCache, TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
		this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<TCache>,
			PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder, FrozenOptions? options = null)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		where TResult : struct, IJoinResult<TValue>
		=> FrozenPlanner.Joined<TKey, TValue, TArgs, TChain, TResolverChain, TResult, ClassicJoinedPlan>(
			builder._leftQuery._cache, in builder._leftQuery._chain, in builder._resolverChain, builder._manyCount, false, options ?? FrozenOptions.Default);
}

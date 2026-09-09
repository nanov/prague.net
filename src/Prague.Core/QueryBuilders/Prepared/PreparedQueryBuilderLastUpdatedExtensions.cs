namespace Prague.Core;

using TypeSystem;

/// <summary>
///   Last-updated narrowing of the prepared builder; see <see cref="PreparedQueryBuilderExtensions" />.
///   Mirrors every eager <c>UseIndex(lastUpdatedIndex, …)</c> overload except the <c>out long max</c>
///   ones: an out-parameter is produced at execution time and has no home on a build-time recorder.
///   Parameterized forms take unix-ms selectors only; convert other time types inside the selector.
/// </summary>
public static class PreparedQueryBuilderLastUpdatedExtensions {
	/// <summary>Rows updated strictly after <paramref name="updatedAfter" />, global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedAfter<TKey, TValue, DateTime, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedAfter<TKey, TValue, DateTime, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" />, global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedBetween<TKey, TValue, DateTime, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter,
			DateTime updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedBetween<TKey, TValue, DateTime, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after <paramref name="updatedAfter" />, global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedAfter<TKey, TValue, DateTimeOffset, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedAfter<TKey, TValue, DateTimeOffset, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" />, global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedBetween<TKey, TValue, DateTimeOffset, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter,
			DateTimeOffset updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedBetween<TKey, TValue, DateTimeOffset, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after <paramref name="updatedAfter" /> (unix ms), global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedAfter<TKey, TValue, long, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			long updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedAfter<TKey, TValue, long, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" /> (unix ms), global last-update index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedBetween<TKey, TValue, long, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			long updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedBetween<TKey, TValue, long, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after a unix-ms instant selected from the execution arguments, global last-update index. Convert DateTime / DateTimeOffset inside the selector.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedAfterArg<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			Func<TArgs, long> updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedAfterArg<TKey, TValue, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated within a unix-ms window (exclusive start, inclusive end) selected from the execution arguments, global last-update index.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, GlobalLastUpdatedBetweenArg<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			IDataCacheGlobalLastUpdateIndex<TKey> lastUpdatedIndex,
			Func<TArgs, long> updatedAfter,
			Func<TArgs, long> updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new GlobalLastUpdatedBetweenArg<TKey, TValue, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after <paramref name="updatedAfter" />, raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedAfter<TKey, TValue, DateTime, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedAfter<TKey, TValue, DateTime, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" />, raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedBetween<TKey, TValue, DateTime, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTime updatedAfter,
			DateTime updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedBetween<TKey, TValue, DateTime, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after <paramref name="updatedAfter" />, raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedAfter<TKey, TValue, DateTimeOffset, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedAfter<TKey, TValue, DateTimeOffset, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" />, raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedBetween<TKey, TValue, DateTimeOffset, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			DateTimeOffset updatedAfter,
			DateTimeOffset updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedBetween<TKey, TValue, DateTimeOffset, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after <paramref name="updatedAfter" /> (unix ms), raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedAfter<TKey, TValue, long, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			long updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedAfter<TKey, TValue, long, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated after <paramref name="updatedAfter" /> up to and including <paramref name="updatedUntilInclusive" /> (unix ms), raw last-updated index, bound now.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedBetween<TKey, TValue, long, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			long updatedAfter,
			long updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedBetween<TKey, TValue, long, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));

	/// <summary>Rows updated strictly after a unix-ms instant selected from the execution arguments, raw last-updated index. Convert DateTime / DateTimeOffset inside the selector.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedAfterArg<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			Func<TArgs, long> updatedAfter)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedAfterArg<TKey, TValue, TArgs>(lastUpdatedIndex, updatedAfter));

	/// <summary>Rows updated within a unix-ms window (exclusive start, inclusive end) selected from the execution arguments, raw last-updated index.</summary>
	public static CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
			PreparedNarrowers<TKey, TValue, TArgs, NarrowerLink<TChain, LastUpdatedBetweenArg<TKey, TValue, TArgs>, TKey, TValue, TArgs>>,
			TKey, TValue, TResolverChain, TResult>
		UseIndex<TKey, TValue, TArgs, TChain, TResolverChain, TResult>(
			this in CacheQueryBuilderCombined<PreparedQueryDiscriminator<InMemoryDataCache<TKey, TValue>>,
				PreparedNarrowers<TKey, TValue, TArgs, TChain>, TKey, TValue, TResolverChain, TResult> builder,
			LastUpdatedIndex<TKey> lastUpdatedIndex,
			Func<TArgs, long> updatedAfter,
			Func<TArgs, long> updatedUntilInclusive)
		where TKey : notnull, IEquatable<TKey>
		where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue>
		where TChain : struct, INarrowerChain<TKey, TValue, TArgs>
		where TResolverChain : struct, IResolvers
		=> PreparedQueryBuilderExtensions.Link(in builder, new LastUpdatedBetweenArg<TKey, TValue, TArgs>(lastUpdatedIndex, updatedAfter, updatedUntilInclusive));
}

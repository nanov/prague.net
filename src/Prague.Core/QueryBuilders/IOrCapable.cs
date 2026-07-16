namespace Prague.Core.QueryBuilders;

using TypeSystem;

/// <summary>
/// Executor capability for the Or clause. Implementers expose:
///   * CreateBranch() — returns a fresh sibling executor that shares
///     inert state (data cache, filter) but starts with empty candidates.
///     The branch lambda then narrows it via WithXxx / UseIndex / nested Or.
///   * OrWith(in branch1, in branch2) — merges the two branch executors'
///     candidate sets into self. Branch candidate storage is disposed by
///     the implementation.
/// </summary>
public interface IOrCapable<TKey, TValue, TSelf>
	where TSelf : struct, IOrCapable<TKey, TValue, TSelf>, ICandidatesExecutor<TKey, TValue>
	where TKey : IEquatable<TKey>, IComparable<TKey>
	where TValue : ICacheEquatable<TValue>, ICacheClonable<TValue> {

	void OrWith<TCache, TResolverChain, TResult, TBranch>(in NarrowOnlyQuery<TCache> narrowOnly,
		TResolverChain resolverChain, in TBranch b1, in TBranch b2)
		where TResolverChain : struct, IResolvers
		where TBranch : struct, IOrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<TCache>, TSelf, TKey, TValue, TResolverChain, TResult>>;
}

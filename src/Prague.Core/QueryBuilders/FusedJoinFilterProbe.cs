namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>
///   Turns a <c>JoinOne</c> filter callback into a per-right check the frozen pipeline's fused fill can run
///   itself (design §7.1, the filtered shapes). A filter callback is a <c>Func&lt;TBuilder, TBuilder&gt;</c>
///   over the paired core, so what it configures is only knowable by running it — but it is known once, at
///   build. This runs it against a <b>probe</b> core that narrows nothing and counts every narrowing
///   attempt, and keeps the composed <c>Where</c> predicate when the callback narrowed nothing else.
///   <para>
///   A pure value predicate is exactly the paired read's own filter: the paired core hands the same
///   delegate to <c>TryGet</c>, which applies it to each right it fetched. Evaluating it on the right the
///   fused lookup just fetched is the same test on the same value, so an outer join's rejected right leaves
///   the slot null and an inner join's drops the row — what the paired walk's "never Added" does.
///   </para>
///   <para>
///   Anything else — an index narrowing, an <c>Or</c> — is a set operation over the pair set, not a
///   predicate over one right, and such a join keeps its paired read (the planner leaves it unfused or
///   replays the chain, exactly as every filtered join did before). The probe counts rather than flags so
///   the completeness test can name the call it saw; every narrowing entry point on the paired core is
///   guarded, and <c>FrozenFusedFilterProbeTests</c> walks the <see cref="ICandidatesFilterer{TKey,TValue}" />
///   interface by reflection to prove it, so an overload added later cannot quietly fuse.
///   </para>
///   The callback runs once more than it used to, at build rather than per execution; a throw out of it is
///   caught and read as "not fusable", which leaves the throw where it was — the first execution.
/// </summary>
internal static class FusedJoinFilterProbe {
	internal static bool TryCompile<TLeft, TRightCache, TRightKey, TRightValue, TFilter>(
		TRightCache cache, TFilter filter, out Predicate<TRightValue>? predicate)
		where TLeft : notnull
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
		where TFilter : struct, IJoinFilter<
			CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeft, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>> {
		predicate = null;
		if (TFilter.IsNoOp)
			return true;
		var probe = new PairedCacheQueryBuilderCoreCombined<TLeft, TRightKey, TRightValue>(cache.Cache, probe: true);
		var builder = new CacheQueryBuilderCombined<
			NonExecutableQuery<TRightCache>,
			PairedCacheQueryBuilderCoreCombined<TLeft, TRightKey, TRightValue>,
			TRightKey, TRightValue,
			Resolvers<BaseResolver<TRightKey, TRightValue>>,
			TRightValue>(
			new NonExecutableQuery<TRightCache>(cache),
			probe,
			new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
			0);
		try {
			builder = filter.Apply(builder);
		} catch {
			// The callback throws at execution too; leave it there rather than at build.
			return false;
		}

		return Unsafe.AsRef(in builder._leftQuery).TryGetProbedPredicate(out predicate);
	}
}

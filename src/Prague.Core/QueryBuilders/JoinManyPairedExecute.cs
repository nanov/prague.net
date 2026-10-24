namespace Prague.Core;

using System.Runtime.CompilerServices;
using TypeSystem;

/// <summary>The one paired execute every fan-out join runs after recording its pairs (the three <c>JoinMany</c> families and the filtered count join).</summary>
internal static class JoinManyPairedExecute {
	/// <summary>
	///   One paired execute over every recorded right: builds the paired core over the fan-out's pair set,
	///   applies the user filter once, then hands the set to the core and delivers each surviving right to all
	///   of its lefts — through the plain paired execute when no right is shared (the FK shape), through a
	///   <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Delivery{TRightValue,TContainer}" /> otherwise. The set
	///   is marked handed off right before the execute — after the filter ran — so a throw inside the user
	///   lambda leaves it to <see cref="JoinManyFanOut{TLeftKey,TRightKey}.Dispose" />, while a set that
	///   reached the core is never disposed twice. The caller disposes the fan-out in a <c>finally</c>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Run<TLeftKey, TRightCache, TRightKey, TRightValue, TFilter, TContainer>(
		TRightCache rightCache, ref TFilter filter, ref JoinManyFanOut<TLeftKey, TRightKey> fanOut, ref TContainer container)
		where TLeftKey : notnull, IEquatable<TLeftKey>
		where TRightKey : notnull, IEquatable<TRightKey>
		where TRightValue : ICacheEquatable<TRightValue>, ICacheClonable<TRightValue>
		where TRightCache : IDataCache<TRightCache, TRightKey, TRightValue>
		where TFilter : struct, IJoinFilter<
			CacheQueryBuilderCombined<NonExecutableQuery<TRightCache>, PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>, TRightKey, TRightValue, Resolvers<BaseResolver<TRightKey, TRightValue>>, TRightValue>>
		where TContainer : struct, IJoinedResultContainer<TLeftKey, TRightValue>, allows ref struct {
		if (fanOut.DistinctRights == 0)
			return;

		var pairedCore = new PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>(rightCache.Cache, fanOut.Pairs);
		var builder = new CacheQueryBuilderCombined<
			NonExecutableQuery<TRightCache>,
			PairedCacheQueryBuilderCoreCombined<TLeftKey, TRightKey, TRightValue>,
			TRightKey, TRightValue,
			Resolvers<BaseResolver<TRightKey, TRightValue>>,
			TRightValue>(
			new NonExecutableQuery<TRightCache>(rightCache),
			pairedCore,
			new Resolvers<BaseResolver<TRightKey, TRightValue>>(new BaseResolver<TRightKey, TRightValue>()),
			0);

		// NoFilter.Apply() is JIT-elided. The filter narrows pairs in place; it cannot add or renumber
		// them. A throw here (user lambda) must leave disposal to the fan-out.
		builder = filter.Apply(builder);

		fanOut.MarkPairsHandedOff();
		ref var core = ref Unsafe.AsRef(in builder._leftQuery);
		if (fanOut.SingleLeftPerRight) {
			// No right is shared, so a pair's JoinedKey is its whole chain: the plain paired execute (the
			// store calls Add(pair.JoinedKey, value)) delivers exactly what a Delivery would, without the
			// wrapper or the chain walk.
			core.ExecutePaired(ref container);
			return;
		}

		var delivery = new JoinManyFanOut<TLeftKey, TRightKey>.Delivery<TRightValue, TContainer>(
			container, fanOut.Heads, fanOut.NodeLefts, fanOut.NodeNexts);
		core.ExecutePairedJoined(ref delivery);
		container = delivery.Inner;
	}
}

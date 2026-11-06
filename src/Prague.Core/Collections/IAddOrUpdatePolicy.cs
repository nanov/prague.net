namespace Prague.Core.Collections;

/// <summary>
///   Decides a value-based <see cref="ConcurrentCacheStore{TKey,TValue}.AddOrUpdate{TPolicy,TArgs}"/> under the bucket
///   lock. Both members run at most once per call and only while the lock is held; a closed struct policy is
///   devirtualized, so an empty <see cref="OnWrite"/> costs nothing.
/// </summary>
internal interface IAddOrUpdatePolicy<TKey, TValue, TArgs> {
	/// <summary>Whether <paramref name="next"/> replaces the resident <paramref name="current"/>; <c>false</c> is Same.</summary>
	static abstract bool ShouldUpdate(TKey key, TValue next, TValue current, ref TArgs args);

	/// <summary>
	///   Runs after the decision to write and before the node is written: on an add with <paramref name="current"/>
	///   <c>null</c>, on an update after <see cref="ShouldUpdate"/> returned <c>true</c>. A throw leaves the store unchanged.
	/// </summary>
	static abstract void OnWrite(TKey key, TValue next, TValue? current, ref TArgs args);
}

/// <summary>The delegate-driven policy behind <see cref="ConcurrentCacheStore{TKey,TValue}.AddOrUpdate(TKey,TValue,Func{TKey,TValue,TValue,bool})"/>.</summary>
internal readonly struct ShouldUpdateFuncPolicy<TKey, TValue> : IAddOrUpdatePolicy<TKey, TValue, Func<TKey, TValue, TValue, bool>> {
	public static bool ShouldUpdate(TKey key, TValue next, TValue current, ref Func<TKey, TValue, TValue, bool> args)
		=> args(key, next, current);

	public static void OnWrite(TKey key, TValue next, TValue? current, ref Func<TKey, TValue, TValue, bool> args) {
	}
}

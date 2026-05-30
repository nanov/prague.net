namespace Prague.Core.Collections;

using System.Collections.Frozen;
using System.Runtime.CompilerServices;

internal sealed class QuickLookupCache<TKey, TValue> where TKey : notnull {
	private FrozenDictionary<TKey, TValue> _cache = FrozenDictionary<TKey, TValue>.Empty;

	public FrozenDictionary<TKey, TValue> Snapshot => _cache;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public TValue GetOrAdd<TParam>(TKey key, Func<TKey, TParam, TValue> factory, TParam param) {
		if (_cache.TryGetValue(key, out var value))
			return value;

		var newValue = factory(key, param);
		FrozenDictionary<TKey, TValue> currentConfig;
		FrozenDictionary<TKey, TValue> newConfig;
		do {
			currentConfig = _cache;
			if (currentConfig.TryGetValue(key, out value))
				return value;

			newConfig = new Dictionary<TKey, TValue>(currentConfig) { { key, newValue } }.ToFrozenDictionary();
		} while (Interlocked.CompareExchange(ref _cache, newConfig, currentConfig) != currentConfig);
		return newValue;
	}

#pragma warning disable CS8619 // Nullability of reference types in value — guarded by Unsafe.IsNullRef
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly TValue GetOrAddRef<TParam>(TKey key, Func<TKey, TParam, TValue> factory, TParam param) {
		ref readonly var value = ref _cache.GetValueRefOrNullRef(key);
		if (!Unsafe.IsNullRef(in value))
			return ref value;

		var newValue = factory(key, param);
		FrozenDictionary<TKey, TValue> currentConfig;
		FrozenDictionary<TKey, TValue> newConfig;
		do {
			currentConfig = _cache;
			value = ref _cache.GetValueRefOrNullRef(key);
			if (!Unsafe.IsNullRef(in value))
				return ref value;

			newConfig = new Dictionary<TKey, TValue>(currentConfig) { { key, newValue } }.ToFrozenDictionary();
		} while (Interlocked.CompareExchange(ref _cache, newConfig, currentConfig) != currentConfig);

		return ref _cache.GetValueRefOrNullRef(key);
	}
#pragma warning restore CS8619
}

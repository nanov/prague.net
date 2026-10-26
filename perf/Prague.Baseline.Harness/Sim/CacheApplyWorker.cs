namespace Prague.Baseline.Harness.Sim;

using Nanov.HighPerformance.Threading.Workers;
using Prague.Baseline.Scenario;

/// <summary>
///   Ring-buffer slot carrying a materialized entity reference plus the message timestamp (ms) the
///   worker replays into the cache. Mirrors the consumer's WorkItem.
/// </summary>
internal struct Slot<T> where T : class {
	public T Value;
	public long TimestampMs;
}

/// <summary>
///   Applies deserialized-and-enriched <see cref="BaselineProduct"/> entities to the generated cache
///   on the worker loop — the real ingest tail. Monomorphic (concrete cache type) to dodge the
///   generic-constraint fight the shared <c>IDataCache&lt;,,&gt;</c> interface would create.
/// </summary>
internal readonly struct ProductApply(BaselineProductCache cache) : IAsyncValueWorkerHandler<Slot<BaselineProduct>> {
	public ValueTask HandleAsync(ref HandleScope<Slot<BaselineProduct>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		cache.AddOrUpdate(value, ts);
		return default;
	}

	public void Clear(ref Slot<BaselineProduct> value) => value.Value = null!;

	public void OnError(Exception error) { }

	public ValueTask OnCompletedAsync(Exception? error) => default;
}

/// <summary>Applies <see cref="BaselineProductInfo"/> entities to the generated cache on the worker loop.</summary>
internal readonly struct InfoApply(BaselineProductInfoCache cache) : IAsyncValueWorkerHandler<Slot<BaselineProductInfo>> {
	public ValueTask HandleAsync(ref HandleScope<Slot<BaselineProductInfo>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		cache.AddOrUpdate(value, ts);
		return default;
	}

	public void Clear(ref Slot<BaselineProductInfo> value) => value.Value = null!;

	public void OnError(Exception error) { }

	public ValueTask OnCompletedAsync(Exception? error) => default;
}

/// <summary>Applies <see cref="BaselineOffer"/> entities to the generated cache on the worker loop.</summary>
internal readonly struct OfferApply(BaselineOfferCache cache) : IAsyncValueWorkerHandler<Slot<BaselineOffer>> {
	public ValueTask HandleAsync(ref HandleScope<Slot<BaselineOffer>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		cache.AddOrUpdate(value, ts);
		return default;
	}

	public void Clear(ref Slot<BaselineOffer> value) => value.Value = null!;

	public void OnError(Exception error) { }

	public ValueTask OnCompletedAsync(Exception? error) => default;
}

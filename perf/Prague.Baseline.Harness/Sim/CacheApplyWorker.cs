namespace Prague.Baseline.Harness.Sim;

using Prague.Baseline.Scenario;
using Prague.Kafka.Internal;

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
///   on the worker thread — the real ingest tail. Monomorphic (concrete cache type) to dodge the
///   generic-constraint fight the shared <c>IDataCache&lt;,,&gt;</c> interface would create.
/// </summary>
internal sealed class ProductApplyWorker : AsyncValueBufferedWorker<Slot<BaselineProduct>> {
	private readonly BaselineProductCache _cache;

	public ProductApplyWorker(BaselineProductCache cache, int capacity)
		: base(capacity, "BaselineSimProductWorker") => _cache = cache;

	protected override ValueTask ProcessAsync(ref ConsumeScope<Slot<BaselineProduct>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		_cache.AddOrUpdate(value, ts);
		return default;
	}
}

/// <summary>Applies <see cref="BaselineProductInfo"/> entities to the generated cache on the worker thread.</summary>
internal sealed class InfoApplyWorker : AsyncValueBufferedWorker<Slot<BaselineProductInfo>> {
	private readonly BaselineProductInfoCache _cache;

	public InfoApplyWorker(BaselineProductInfoCache cache, int capacity)
		: base(capacity, "BaselineSimInfoWorker") => _cache = cache;

	protected override ValueTask ProcessAsync(ref ConsumeScope<Slot<BaselineProductInfo>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		_cache.AddOrUpdate(value, ts);
		return default;
	}
}

/// <summary>Applies <see cref="BaselineOffer"/> entities to the generated cache on the worker thread.</summary>
internal sealed class OfferApplyWorker : AsyncValueBufferedWorker<Slot<BaselineOffer>> {
	private readonly BaselineOfferCache _cache;

	public OfferApplyWorker(BaselineOfferCache cache, int capacity)
		: base(capacity, "BaselineSimOfferWorker") => _cache = cache;

	protected override ValueTask ProcessAsync(ref ConsumeScope<Slot<BaselineOffer>> scope, CancellationToken cancellationToken) {
		ref var slot = ref scope.Event();
		var value = slot.Value;
		var ts = slot.TimestampMs;
		scope.Release();
		_cache.AddOrUpdate(value, ts);
		return default;
	}
}

namespace Prague.Kafka;

/// <summary>
///   Changes a consumed value in place before the cache stores it. Registered per cache with
///   <see cref="KafkaCacheHandlerBuilder{TCacheEntity,TKey,TValue}.WithEnricher{TEnricher}()" />.
///   <para>
///     Runs for every value that passed the header, key and value filters, in both the initial load and the live
///     phase, after Prague's own <c>[DataCacheHeader]</c> / <c>[DataCacheFromTimestamp]</c> enrichment and before
///     the value enters the load buffer or the live worker. The value is not yet visible to any reader, so it is
///     changed in place; the indexes are built from the enriched value, and the live phase compares the enriched
///     value with the resident one to decide <see cref="UpdateType.Same" /> against <see cref="UpdateType.Update" />.
///   </para>
///   <para>
///     Runs synchronously on the single consume thread shared by every cache of the <c>AddKafkaCaches</c> section,
///     so it must not block or perform I/O: a dictionary lookup or a <c>TryGet</c> on another cache is the intended
///     cost. Another Prague cache read from here holds whatever its own consumption has reached — complete once
///     both caches are live, possibly partial while the section is still loading, because topics load concurrently.
///   </para>
///   <para>
///     A throw is logged (<c>EnricherError</c>) and the value is stored as enriched so far; the message is never
///     dropped and the remaining enrichers of that value still run. Several enrichers run in registration order.
///   </para>
/// </summary>
/// <typeparam name="TKey">The cache key type.</typeparam>
/// <typeparam name="TValue">The cache value type.</typeparam>
public interface ICacheEnricher<in TKey, in TValue> {
	/// <summary>Fills or changes fields of <paramref name="value" /> before it is stored.</summary>
	/// <param name="key">The deserialized key of the message.</param>
	/// <param name="value">The deserialized, header-enriched value; not yet in the cache.</param>
	void Enrich(TKey key, TValue value);
}

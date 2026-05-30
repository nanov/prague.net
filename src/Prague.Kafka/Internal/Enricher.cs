namespace Prague.Kafka.Internal;

using Confluent.Kafka;

/// <summary>
///   Base class for enriching cache items with Kafka message metadata.
/// </summary>
public abstract class Enricher<TValue> {
	/// <summary>
	///   Enriches the cache item with Kafka message metadata.
	/// </summary>
	public abstract void Enrich(TValue entity, Headers headers, Timestamp timestamp);
}

/// <summary>
///   No-op enricher for types without timestamp properties.
/// </summary>
public sealed class NoOpEnricher<TValue> : Enricher<TValue> {
	public static readonly NoOpEnricher<TValue> Instance = new();

	private NoOpEnricher() {
	}

	public override void Enrich(TValue entity, Headers headers, Timestamp timestamp) {
		// No-op
	}
}
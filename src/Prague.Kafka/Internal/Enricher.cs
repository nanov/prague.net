namespace Prague.Kafka.Internal;

using Confluent.Kafka;

/// <summary>
///   Base class for enriching cache items with Kafka message metadata.
/// </summary>
public abstract class Enricher<TValue> {
	/// <summary>
	///   Zero-allocation enrichment from the raw consume-path headers (UTF-8 name + value spans into
	///   librdkafka's buffer) plus the message timestamp.
	/// </summary>
	public abstract void Enrich(TValue entity, in RawHeaders headers, Timestamp timestamp);
}

/// <summary>
///   No-op enricher for types without timestamp properties.
/// </summary>
public sealed class NoOpEnricher<TValue> : Enricher<TValue> {
	public static readonly NoOpEnricher<TValue> Instance = new();

	private NoOpEnricher() {
	}

	public override void Enrich(TValue entity, in RawHeaders headers, Timestamp timestamp) {
		// No-op
	}
}
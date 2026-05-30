namespace Prague.Kafka;

using Confluent.Kafka;
using Internal;

public interface IKafkaCache {
	public string Topic { get; }
	void CreateDump(string path);
}

/// <summary>
///   Marker interface for cache items that can be enriched with Kafka metadata.
/// </summary>
/// <typeparam name="TSelf">The implementing type (for static abstract interface members pattern)</typeparam>
public interface IEnrichable<TSelf> where TSelf : IEnrichable<TSelf> {
	/// <summary>
	///   Gets the enricher for this cache item type.
	/// </summary>
	static abstract Enricher<TSelf> GetEnricher();

	/// <summary>
	///   Extracts values from entity and adds them to Kafka headers (dericher).
	/// </summary>
	static abstract void Derich(TSelf entity, Headers headers);
}

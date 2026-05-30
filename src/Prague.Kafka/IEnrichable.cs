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
	///   Extracts values from entity and adds them to Kafka headers (dericher). Used by dump/restore.
	/// </summary>
	static abstract void Derich(TSelf entity, Headers headers);

	/// <summary>
	///   Allocation-free dericher for the raw produce path — appends header values into the inline
	///   <see cref="KafkaHeaders"/> struct instead of the managed <see cref="Headers"/> collection.
	/// </summary>
	static abstract void Derich(TSelf entity, ref KafkaHeaders headers);
}

namespace Prague.Kafka;

using IO;

/// <summary>
///   Interface for caches that can be configured with a Kafka producer.
/// </summary>
/// <typeparam name="TSelf">The implementing cache type</typeparam>
public interface IKafkaProducerConfigurable<TSelf> where TSelf : IKafkaProducerConfigurable<TSelf> {
	/// <summary>
	///   Configures the Kafka producer for a cache instance.
	/// </summary>
	static abstract void ConfigureProducer(TSelf cache, KafkaCacheProducer producer);
}
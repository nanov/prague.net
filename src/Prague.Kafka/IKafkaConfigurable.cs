namespace Prague.Kafka;

/// <summary>
///   Interface for caches that can be configured with a Kafka topic.
/// </summary>
/// <typeparam name="TSelf">The implementing cache type</typeparam>
public interface IKafkaConfigurable<TSelf> where TSelf : IKafkaConfigurable<TSelf> {
	/// <summary>
	///   Configures the Kafka topic for a cache instance.
	/// </summary>
	static abstract void ConfigureTopic(TSelf cache, string topicName);

	/// <summary>
	///   Configures the partition count of the cache's topic, read from topic metadata at startup. Generated caches
	///   implement it; the empty default keeps a cache written before partitions compiling.
	/// </summary>
	static virtual void ConfigurePartitionCount(TSelf cache, int partitionCount) { }
}

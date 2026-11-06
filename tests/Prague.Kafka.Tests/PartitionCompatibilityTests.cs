namespace Prague.Kafka.Tests;

using Core;

/// <summary>
///   Implementations written before partitions existed must keep compiling: these two types implement the
///   interfaces exactly as they were, and the test only has to build and run.
/// </summary>
public sealed class PartitionCompatibilityTests {
	[Test]
	public void Pre_partition_metadata_implementation_receives_the_three_argument_call() {
		IPragueMetadataSettable item = new PrePartitionItem();
		item.SetPragueMetadata(10, 20, 3);

		Assert.That(item.__PragueMetadata__.Offset, Is.EqualTo(20));
		Assert.That(item.__PragueMetadata__.Partition, Is.EqualTo(0), "the old form records partition 0");
	}

	[Test]
	public void Pre_partition_configurable_cache_gets_the_empty_default() =>
		Assert.DoesNotThrow(() => Configure<PrePartitionCache>(new PrePartitionCache(), 3));

	private static void Configure<T>(T cache, int count) where T : IKafkaConfigurable<T> =>
		T.ConfigurePartitionCount(cache, count);

	private sealed class PrePartitionItem : IPragueMetadataSettable {
		public PersistenceInfo __PragueMetadata__ { get; private set; }
		public void SetPragueMetadata(long timestamp, long offset) => __PragueMetadata__ = new PersistenceInfo(timestamp, offset);
	}

	private sealed class PrePartitionCache : IKafkaConfigurable<PrePartitionCache> {
		public static void ConfigureTopic(PrePartitionCache cache, string topicName) { }
	}
}

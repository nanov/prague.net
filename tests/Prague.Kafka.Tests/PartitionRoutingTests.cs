namespace Prague.Kafka.Tests;

using Core;

public sealed class PartitionRoutingTests {
	private const int Unknown = PersistenceInfo.UnknownPartition;

	[TestCase(Unknown, Unknown, ExpectedResult = Unknown, TestName = "new key, no partition: partitioner chooses")]
	[TestCase(Unknown, 2, ExpectedResult = 2, TestName = "resident key, no partition: stored partition")]
	[TestCase(1, Unknown, ExpectedResult = 1, TestName = "new key, explicit partition")]
	[TestCase(2, 2, ExpectedResult = 2, TestName = "resident key, matching explicit partition")]
	public int Resolves(int requested, int stored) => PartitionRouting.Resolve(requested, stored, 3, "t");

	[Test]
	public void Explicit_partition_differing_from_the_stored_one_throws() =>
		Assert.Throws<InvalidOperationException>(() => PartitionRouting.Resolve(0, 2, 3, "t"));

	[TestCase(3)]
	[TestCase(-2)]
	public void Partition_outside_the_topic_throws(int requested) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => PartitionRouting.Resolve(requested, Unknown, 3, "t"));

	[Test]
	public void Unknown_partition_count_skips_only_the_upper_bound() {
		Assert.That(PartitionRouting.Resolve(7, Unknown, 0, "t"), Is.EqualTo(7));
		Assert.Throws<ArgumentOutOfRangeException>(() => PartitionRouting.Resolve(-2, Unknown, 0, "t"));
	}

	[Test]
	public void Default_persistence_info_has_an_unknown_partition() {
		Assert.That(default(PersistenceInfo).Partition, Is.EqualTo(Unknown));
		Assert.That(new PersistenceInfo(1, 2, 1).Partition, Is.EqualTo(1));
		Assert.That(new PersistenceInfo(1, 2).Partition, Is.EqualTo(0), "the pre-partition constructor means partition 0");
	}
}

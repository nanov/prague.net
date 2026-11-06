namespace Prague.Kafka.Tests;

using System.Text.Json;
using Nanov.Kafka;
using Core;
using Prague.Kafka.Filters;
using IO;
using Utils;

/// <summary>
///   Broker-free coverage of the per-partition load handshake and the per-topic stats aggregation. The
///   broker-backed end-to-end behaviour is in <c>MultiPartitionLoadTests</c>.
/// </summary>
public sealed class PartitionLoadHandshakeTests {
	[Test]
	public void Load_completes_only_when_every_partition_reaches_its_own_watermark() {
		var handler = new StubHandler();
		Assert.That(handler.TrackAssignedPartition(0, 0), Is.True);
		Assert.That(handler.TrackAssignedPartition(1, 10), Is.True);
		Assert.That(handler.TrackAssignedPartition(2, 500), Is.True);

		Assert.That(handler.TryCompletePartitionLoad(0, 0), Is.False, "empty partition alone must not complete the load");
		Assert.That(handler.TryCompletePartitionLoad(2, 499), Is.False, "EOF below the watermark is mid-backlog");
		Assert.That(handler.TryCompletePartitionLoad(1, 10), Is.False);
		Assert.That(handler.TryCompletePartitionLoad(0, 0), Is.False, "a repeated EOF must not count twice");
		Assert.That(handler.TryCompletePartitionLoad(2, 500), Is.True);
	}

	[Test]
	public void Load_waits_for_partitions_of_the_topic_not_yet_assigned() {
		var handler = new StubHandler();
		handler.SetExpectedPartitionCount(3);
		handler.TrackAssignedPartition(0, 1);
		handler.TrackAssignedPartition(1, 1);

		Assert.That(handler.TryCompletePartitionLoad(0, 1), Is.False);
		Assert.That(handler.TryCompletePartitionLoad(1, 1), Is.False, "partition 2 exists but was not assigned yet");

		handler.TrackAssignedPartition(2, 0);
		Assert.That(handler.TryCompletePartitionLoad(2, 0), Is.True);
	}

	[Test]
	public void Reassigned_partition_keeps_its_state() {
		var handler = new StubHandler();
		handler.TrackAssignedPartition(0, 5);
		handler.TrackAssignedPartition(1, 5);
		handler.TryCompletePartitionLoad(0, 5);

		// Re-assigned with a later watermark: neither re-counted as pending nor given the new watermark.
		Assert.That(handler.TrackAssignedPartition(0, 9), Is.True);
		Assert.That(handler.TrackAssignedPartition(1, 9), Is.True);
		Assert.That(handler.TryCompletePartitionLoad(1, 5), Is.True);
	}

	[Test]
	public void Partition_first_seen_after_load_is_reported_unsupported() {
		var handler = new StubHandler();
		handler.TrackAssignedPartition(0, 1);
		handler.TryCompletePartitionLoad(0, 1);
		handler.IsInitialConsumeDone = true;

		Assert.That(handler.TrackAssignedPartition(0, 1), Is.True, "re-assignment of a known partition is fine");
		Assert.That(handler.TrackAssignedPartition(1, 0), Is.False);
	}

	[Test]
	public void Topic_stats_sum_real_partitions_and_report_the_least_advanced_fetch_state() {
		var json = """
		{
		  "topics": { "t": { "partitions": {
		    "-1": { "rxmsgs": 1000, "rxbytes": 1000, "fetchq_cnt": 1000, "fetchq_size": 1000, "fetch_state": "none" },
		    "0":  { "rxmsgs": 1, "rxbytes": 10, "fetchq_cnt": 2, "fetchq_size": 20, "fetch_state": "active" },
		    "1":  { "rxmsgs": 3, "rxbytes": 30, "fetchq_cnt": 4, "fetchq_size": 40, "fetch_state": "offset-wait" },
		    "2":  { "rxmsgs": 5, "rxbytes": 50, "fetchq_cnt": 6, "fetchq_size": 60, "fetch_state": "active" }
		  } } }
		}
		""";
		var snapshot = JsonSerializer.Deserialize(json, LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);
		var handler = new StubHandler();

		handler.UpdateTopicStats(snapshot.Topics!["t"]);

		Assert.That(handler.Statistics.TotalMessagesReceived, Is.EqualTo(9));
		Assert.That(handler.Statistics.TotalBytesReceived, Is.EqualTo(90));
		Assert.That(handler.Statistics.FetchQueueCount, Is.EqualTo(12));
		Assert.That(handler.Statistics.FetchQueueSize, Is.EqualTo(120));
		Assert.That(handler.Statistics.FetchState, Is.EqualTo(FetchState.OffsetWait));
	}

	private sealed class StubHandler() : KafkaCacheHandler(KafkaHeaderFilters.Create(null)) {
		public override KafkaDataCacheStatistics Statistics { get; } =
			new("t", new DataCacheStatistics(DataCacheNoOpStatisticsCollector.Default));

		public override string Name => "stub";
		public override Task WaitForCompletionAsync() => Task.CompletedTask;
		internal override void DispatchRaw(in RawMessage raw, bool isLoading) { }
		internal override void PublishRawFiltered() { }
		internal override void FlushRawLoadBufferAndGoLive(AsyncCountdownEvent countdownEvent, long offset, CancellationToken ct) { }
		internal override void StopRawWorker() { }
	}
}

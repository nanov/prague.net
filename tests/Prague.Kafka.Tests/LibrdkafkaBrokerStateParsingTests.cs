namespace Prague.Kafka.Tests;

using System.Text.Json;

public sealed class LibrdkafkaBrokerStateParsingTests {
	[Test]
	public void UpdateFromLibrdkafkaStats_counts_brokers_with_state_UP() {
		var json = """
		{
		  "rxmsgs": 0,
		  "rxmsg_bytes": 0,
		  "brokers": {
		    "b1": { "state": "UP",   "rtt": {}, "throttle": {}, "int_latency": {} },
		    "b2": { "state": "DOWN", "rtt": {}, "throttle": {}, "int_latency": {} },
		    "b3": { "state": "UP",   "rtt": {}, "throttle": {}, "int_latency": {} }
		  }
		}
		""";
		var snapshot = JsonSerializer.Deserialize(json,
			LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);

		var stats = new KafkaCachesConsumerStatistics();
		stats.UpdateFromLibrdkafkaStats(snapshot);

		Assert.That(stats.BrokerUpCount, Is.EqualTo(2));
	}

	[Test]
	public void UpdateFromLibrdkafkaStats_with_no_brokers_sets_zero() {
		var json = """{"rxmsgs":0,"rxmsg_bytes":0}""";
		var snapshot = JsonSerializer.Deserialize(json,
			LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);

		var stats = new KafkaCachesConsumerStatistics();
		stats.UpdateFromLibrdkafkaStats(snapshot);

		Assert.That(stats.BrokerUpCount, Is.EqualTo(0));
	}
}

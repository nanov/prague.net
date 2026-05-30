namespace Prague.Kafka;

using Confluent.Kafka;

internal static class KafkaCaches {
	public const string ProducerInstanceIdHeaderName = "X-Producer-Id";

	public static readonly Guid InstanceId = Guid.NewGuid();
	public static readonly byte[] InstanceIdBytes = InstanceId.ToByteArray();

	public static Header ProducerInstanceHeader => new(ProducerInstanceIdHeaderName, InstanceIdBytes);
}
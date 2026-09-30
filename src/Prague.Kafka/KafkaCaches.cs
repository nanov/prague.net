namespace Prague.Kafka;

using Confluent.Kafka;

internal static class KafkaCaches {
	public const string ProducerInstanceIdHeaderName = "X-Producer-Id";

	/// <summary>
	///   Marks a forced write: a consumer applies the message unconditionally, bypassing the
	///   <c>CacheEquals</c> gate, in both the load and the live phase. Presence is the signal; the value is
	///   reserved and never inspected. Any producer may stamp it — the trust model is the same as for the
	///   instance-id header and the user header filters.
	/// </summary>
	public const string ForceHeaderName = "X-Prague-Force";

	public static readonly Guid InstanceId = Guid.NewGuid();
	public static readonly byte[] InstanceIdBytes = InstanceId.ToByteArray();
	public static readonly byte[] ForceHeaderValue = "1"u8.ToArray();

	public static Header ProducerInstanceHeader => new(ProducerInstanceIdHeaderName, InstanceIdBytes);
}

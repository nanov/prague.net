namespace Prague.Kafka.Dump;

using MessagePack;

[MessagePackObject]
public sealed class PragueConsumeResult {
	[Key(0)] public string Topic { get; set; } = "";
	[Key(1)] public int Partition { get; set; }
	[Key(2)] public long Offset { get; set; }
	[Key(3)] public long TimestampMs { get; set; }
	[Key(4)] public byte[]? Key { get; set; }
	[Key(5)] public byte[]? Value { get; set; }
	[Key(6)] public List<PragueHeader>? Headers { get; set; }
}

[MessagePackObject]
public sealed class PragueHeader {
	[Key(0)] public string Key { get; set; } = "";
	[Key(1)] public byte[]? Value { get; set; }
}

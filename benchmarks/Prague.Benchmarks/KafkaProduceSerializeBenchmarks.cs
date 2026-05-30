namespace Prague.Benchmarks;

using BenchmarkDotNet.Attributes;
using Confluent.Kafka;
using Prague.Kafka;
using Prague.Kafka.Internal;
using Prague.Kafka.SerDe;

/// <summary>
///   Per-produce managed-allocation comparison of the producer's serialize + header-build step:
///   the original path (MessagePack → fresh <c>byte[]</c> per key and value + a managed
///   <see cref="Headers"/>/<see cref="Header"/> graph) vs the new path (serialize into a pooled
///   <c>ScratchArrayWriter</c> + an inline <see cref="KafkaHeaders"/> struct). The actual
///   <c>RawProduce</c> call isn't included (needs a broker); this isolates the managed allocations
///   the producer controls, which RawProduce then copies (MSG_F_COPY).
/// </summary>
[MemoryDiagnoser]
public class KafkaProduceSerializeBenchmarks {
	private const int MessageCount = 1000;

	private int _key;
	private LiveDispatchEntity _value = null!;

	[GlobalSetup]
	public void Setup() {
		_key = 42;
		_value = new LiveDispatchEntity { Id = 42, Name = "benchmark-entity-payload" };
	}

	/// <summary>Original path: fresh byte[] for key and value + a managed Headers graph per produce.</summary>
	[Benchmark(Baseline = true)]
	public long TypedSerialize() {
		var total = 0L;
		for (var i = 0; i < MessageCount; i++) {
			var keyBytes = CacheSerde<int>.Serialize(_key);
			var valueBytes = CacheSerde<LiveDispatchEntity>.Serialize(_value);
			var headers = new Headers { KafkaCaches.ProducerInstanceHeader };
			LiveDispatchEntity.Derich(_value, headers);
			total += keyBytes.Length + valueBytes.Length + headers.Count;
		}

		return total;
	}

	/// <summary>New path: serialize into pooled scratch writers + inline KafkaHeaders (no per-produce byte[] / Headers graph).</summary>
	[Benchmark]
	public long ScratchSerialize() {
		var total = 0L;
		for (var i = 0; i < MessageCount; i++) {
			var keyWriter = ScratchArrayWriterManager<int>.Rent();
			var valueWriter = ScratchArrayWriterManager<LiveDispatchEntity>.Rent();
			try {
				CacheSerde<int>.SerializeInto(_key, keyWriter);
				CacheSerde<LiveDispatchEntity>.SerializeInto(_value, valueWriter);
				var headers = new KafkaHeaders();
				headers.Add(KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes);
				LiveDispatchEntity.Derich(_value, ref headers);
				total += keyWriter.WrittenSpan.Length + valueWriter.WrittenSpan.Length + headers.Count;
			}
			finally {
				ScratchArrayWriterManager<int>.Return(keyWriter);
				ScratchArrayWriterManager<LiveDispatchEntity>.Return(valueWriter);
			}
		}

		return total;
	}
}

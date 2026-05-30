namespace Prague.Benchmarks;

using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Confluent.Kafka;
using MessagePack;
using Prague.Core;
using Prague.Kafka;
using Prague.Kafka.Internal;
using Prague.Kafka.SerDe;

/// <summary>
///   Per-message managed-allocation comparison of the consumer's live (after-initial-load) dispatch:
///   the original typed-channel path vs the new RawConsumer + zero-copy + ring-buffer-worker path.
///
///   Both paths deserialize the same entity and run the same (timestamp-only) enrichment, so those
///   allocations are equal — the reported delta is the *structural* overhead the new path removes:
///   stock Confluent's per-message <see cref="ConsumeResult{TKey,TValue}"/> +
///   <see cref="Message{TKey,TValue}"/> + <see cref="Headers"/> graph (which <c>RawConsumer</c>
///   replaces with a stack-only <c>RawMessage</c>) plus the bounded-channel handoff (replaced by the
///   zero-alloc ring-buffer worker).
///
///   The baseline's hand-built <see cref="ConsumeResult{TKey,TValue}"/> graph stands in for what
///   librdkafka's managed bindings allocate per consumed message — there is no broker here. The new
///   path's value bytes live in native memory (mirroring librdkafka's buffer) and are deserialized
///   zero-copy via <see cref="SpanMessagePackDeserializer"/>.
/// </summary>
[MemoryDiagnoser]
public class KafkaLiveDispatchBenchmarks {
	private const int MessageCount = 1000;

	private byte[] _valueBytes = null!;
	private byte[] _keyBytes = null!;
	private byte[] _producerId = null!;
	private Prague.Kafka.Internal.Enricher<LiveDispatchEntity> _enricher = null!;
	private NoOpWorker _worker = null!;

	// Native copy of the value payload — mirrors librdkafka's buffer for the zero-copy path.
	private unsafe byte* _nativeValue;
	private int _nativeValueLen;

	[GlobalSetup]
	public unsafe void Setup() {
		var sample = new LiveDispatchEntity { Id = 42, Name = "benchmark-entity-payload" };
		_valueBytes = CacheSerde<LiveDispatchEntity>.Serialize(sample);
		_keyBytes = CacheSerde<int>.Serialize(42);
		_producerId = Guid.NewGuid().ToByteArray();
		_enricher = LiveDispatchEntity.GetEnricher();

		_nativeValueLen = _valueBytes.Length;
		_nativeValue = (byte*)NativeMemory.Alloc((nuint)_nativeValueLen);
		_valueBytes.AsSpan().CopyTo(new Span<byte>(_nativeValue, _nativeValueLen));

		_worker = new NoOpWorker(64);
		_worker.Start();
	}

	[GlobalCleanup]
	public unsafe void Cleanup() {
		_worker.Dispose();
		if (_nativeValue is not null)
			NativeMemory.Free(_nativeValue);
	}

	/// <summary>
	///   Original path: the managed graph stock Confluent hands per message (ConsumeResult + Message +
	///   Headers), then pooled-byte deserialize + enrich. The value buffer is rented + returned
	///   (pooled, as in the real consumer), so it doesn't allocate — only the graph does.
	/// </summary>
	[Benchmark(Baseline = true)]
	public long TypedChannelPath() {
		var total = 0L;
		for (var i = 0; i < MessageCount; i++) {
			var headers = new Headers {
				new Header("X-Producer-Id", _producerId)
			};
			var message = new Message<byte[], byte[]> {
				Key = _keyBytes,
				Value = _valueBytes,
				Headers = headers,
				Timestamp = new Timestamp(1_700_000_000_000 + i, TimestampType.CreateTime)
			};
			var result = new ConsumeResult<byte[], byte[]> {
				Message = message,
				Topic = "live-dispatch-bench",
				Partition = new Partition(0),
				Offset = new Offset(i)
			};

			var value = MessagePackSerializer.Deserialize<LiveDispatchEntity>(result.Message.Value, PragueMessagePack.Options);
			_enricher.Enrich(value, default(RawHeaders), result.Message.Timestamp);
			total += value.CreatedAt;
		}

		return total;
	}

	/// <summary>
	///   New path: zero-copy deserialize straight off the native value span, enrich from the (empty)
	///   raw headers, publish the materialized entity into the ring-buffer worker. No ConsumeResult
	///   graph, no Headers object, no channel.
	/// </summary>
	[Benchmark]
	public unsafe long RawWorkerPath() {
		var total = 0L;
		for (var i = 0; i < MessageCount; i++) {
			var valueSpan = new ReadOnlySpan<byte>(_nativeValue, _nativeValueLen);
			var value = CacheSerde<LiveDispatchEntity>.DeserializeFromSpan(valueSpan);
			_enricher.Enrich(value, default(RawHeaders), new Timestamp(1_700_000_000_000 + i, TimestampType.CreateTime));
			total += value.CreatedAt;

			using var scope = _worker.Publish();
			if (scope.IsOpen) {
				ref var slot = ref scope.Event();
				slot.Value = value;
			}
		}

		return total;
	}

	/// <summary>Ring-buffer slot — a struct carrying the materialized entity reference (mirrors the consumer's WorkItem).</summary>
	private struct Slot {
		public LiveDispatchEntity Value;
	}

	/// <summary>No-op ring-buffer worker — reads the slot and releases it; measures pure handoff cost.</summary>
	private sealed class NoOpWorker : AsyncValueBufferedWorker<Slot> {
		public NoOpWorker(int capacity) : base(capacity, "BenchRawWorker") { }

		protected override ValueTask ProcessAsync(ref ConsumeScope<Slot> scope, CancellationToken cancellationToken) {
			scope.Release();
			return default;
		}
	}
}

/// <summary>Timestamp-only Kafka cache entity for the dispatch benchmark — enrichment reads no headers.</summary>
[DataCache]
public partial class LiveDispatchEntity : IDataCacheItem<int, LiveDispatchEntity> {
	public int Id { get; set; }
	public string Name { get; set; } = "";

	[DataCacheFromTimestamp] public long CreatedAt { get; set; }

	public int GetKey() => Id;
	public void SetKey(int key) => Id = key;
}

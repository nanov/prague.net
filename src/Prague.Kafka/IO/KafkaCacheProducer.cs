namespace Prague.Kafka.IO;

using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Internal;
using Microsoft.Extensions.Logging;
using Options;
using SerDe;
using Utils;

public class KafkaCacheProducer : IDisposable {
	private readonly ILogger<KafkaCacheProducer> _logger;
	private const int MAX_RETRIES = 5;
	private const int MAX_RETRIES_MINUS_ONE = MAX_RETRIES - 1;
	private const int BACKOFF_MS = 50;

	private readonly CancellationTokenSource _cts = new();
	private readonly IRawProducer _producer;

	internal KafkaCacheProducer(IKafkaCacheBuilderProvider kafkaCacheBuilderProvider, KafkaCachesOptions configuration,
		ILogger<KafkaCacheProducer> logger) {
		_logger = logger;
		var builder = kafkaCacheBuilderProvider.NewRawProducerBuilder(
			new ProducerConfig(configuration.ClientSettings.ToDictionary(kv => kv.Key, kv => kv.Value)) {
				BootstrapServers = configuration.BootstrapServers,
				CompressionType = CompressionType.Gzip,
				AllowAutoCreateTopics = false
				// TODO: propersetup
			});
		builder.SetErrorHandler((_, e) => { _logger.LogError("Kafka caches consumer error: {Error}", e.Reason); });
		// TODO: add logging and error handling

		_producer = builder.BuildRaw();
	}

	public void Dispose() {
		GC.SuppressFinalize(this);
		_cts.Cancel();
		_producer.Flush();
		_producer.Dispose();
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Delete<TKey>(string topic, TKey key) {
		if (_cts.IsCancellationRequested)
			return;

		var keyWriter = ScratchArrayWriterManager<TKey>.Rent();
		try {
			CacheSerde<TKey>.SerializeInto(key, keyWriter);
			var headers = new KafkaHeaders();
			headers.Add(KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes);
			// Null/empty value == tombstone (delete).
			RawProduceWithRetry(topic, keyWriter.WrittenSpan, default, in headers);
		}
		finally {
			ScratchArrayWriterManager<TKey>.Return(keyWriter);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Produce<TKey, TCacheValue>(string topic, TKey key, TCacheValue value)
		where TKey : notnull, IEquatable<TKey>, IComparable<TKey>
		where TCacheValue : IEnrichable<TCacheValue> {
		if (_cts.IsCancellationRequested)
			return;

		var keyWriter = ScratchArrayWriterManager<TKey>.Rent();
		var valueWriter = ScratchArrayWriterManager<TCacheValue>.Rent();
		try {
			CacheSerde<TKey>.SerializeInto(key, keyWriter);
			CacheSerde<TCacheValue>.SerializeInto(value, valueWriter);

			var headers = new KafkaHeaders();
			headers.Add(KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes);
			TCacheValue.Derich(value, ref headers);

			RawProduceWithRetry(topic, keyWriter.WrittenSpan, valueWriter.WrittenSpan, in headers);
		}
		finally {
			ScratchArrayWriterManager<TKey>.Return(keyWriter);
			ScratchArrayWriterManager<TCacheValue>.Return(valueWriter);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private void RawProduceWithRetry(string topic, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, in KafkaHeaders headers) {
		for (var i = 0; i < MAX_RETRIES; i++)
			try {
				_producer.RawProduce(topic, key, value, in headers);
				break;
			}
			catch (KafkaException e) when (e.Error.Code == ErrorCode.Local_QueueFull) {
				if (i == MAX_RETRIES_MINUS_ONE)
					throw;

				// don't do heavy context change and spread async/await, but don't block the producing thread
				if (ValueSpinWait.SpinUntil(static cts => cts.IsCancellationRequested, BACKOFF_MS, _cts))
					break; // cancellation requested;
			}
	}
}

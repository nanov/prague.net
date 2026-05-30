namespace Prague.Kafka.IO;

using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Options;
using SerDe;
using Utils;

public class KafkaCacheProducer : IDisposable {
	private readonly ILogger<KafkaCacheProducer> _logger;
	private const int MAX_RETRIES = 5;
	private const int MAX_RETRIES_MINUS_ONE = MAX_RETRIES - 1;
	private const int BACKOFF_MS = 50;

	private static readonly Headers InstanceOnlyHeaders = new() { KafkaCaches.ProducerInstanceHeader };

	private readonly CancellationTokenSource _cts = new();
	private readonly IProducer<byte[], byte[]> _producer;

	internal KafkaCacheProducer(IKafkaCacheBuilderProvider kafkaCacheBuilderProvider, KafkaCachesOptions configuration,
		ILogger<KafkaCacheProducer> logger) {
		_logger = logger;
		var builder = kafkaCacheBuilderProvider.NewProducerBuilder<byte[], byte[]>(
				new ProducerConfig(configuration.ClientSettings.ToDictionary(kv => kv.Key, kv => kv.Value)) {
					BootstrapServers = configuration.BootstrapServers,
					CompressionType = CompressionType.Gzip,
					AllowAutoCreateTopics = false,
					// TODO: propersetup
				})
			.SetErrorHandler((c, e) => { _logger.LogError("Kafka caches consumer error: {Error}", e.Reason); });
		// TODO: add logging and error handling

		_producer = builder.Build();
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

		var keyBytes = CacheSerde<TKey>.Serialize(key);
		var headers = InstanceOnlyHeaders;
		for (var i = 0; i < MAX_RETRIES; i++)
			try {
				_producer.Produce(topic, new Message<byte[], byte[]> { Key = keyBytes, Value = null!, Headers = headers });
				break;
			}
			catch (ProduceException<byte[], byte[]> e) when (e.Error.Code == ErrorCode.Local_QueueFull) {
				if (i == MAX_RETRIES_MINUS_ONE)
					throw;

				// don't do heavy context change and spread async/await, but don't block the producing thread
				if (ValueSpinWait.SpinUntil(static cts => cts.IsCancellationRequested, BACKOFF_MS, _cts))
					break; // cancellation requested;
			}
	}


	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public void Produce<TKey, TCacheValue>(string topic, TKey key, TCacheValue value)
		where TKey : notnull, IEquatable<TKey>
		where TCacheValue : IEnrichable<TCacheValue> {
		if (_cts.IsCancellationRequested)
			return;

		var keyBytes = CacheSerde<TKey>.Serialize(key);
		var valueBytes = CacheSerde<TCacheValue>.Serialize(value);
		var headers = new Headers { KafkaCaches.ProducerInstanceHeader };
		TCacheValue.Derich(value, headers);

		for (var i = 0; i < MAX_RETRIES; i++)
			try {
				_producer.Produce(topic, new Message<byte[], byte[]> { Key = keyBytes, Value = valueBytes, Headers = headers });
				break;
			}
			catch (ProduceException<byte[], byte[]> e) when (e.Error.Code == ErrorCode.Local_QueueFull) {
				if (i == MAX_RETRIES_MINUS_ONE)
					throw;

				// don't do heavy context change and spread async/await, but don't block the producing thread
				if (ValueSpinWait.SpinUntil(static cts => cts.IsCancellationRequested, BACKOFF_MS, _cts))
					break; // cancellation requested;
			}
	}
}

namespace Prague.Kafka.IO;

using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Confluent.Kafka;
using Core;
using Filters;
using Internal;
using Microsoft.Extensions.Logging;
using Options;
using SerDe;
using Utils;

internal abstract class KafkaCacheHandler {
	public readonly KafkaHeaderFilters HeadersFilters;
	public readonly SpscByteRingBuffer KeyRingBuffer;

	public KafkaCacheHandler(KafkaHeaderFilters headersFilters, int keyRingBufferSize) {
		HeadersFilters = headersFilters;
		KeyRingBuffer = new SpscByteRingBuffer(keyRingBufferSize);
	}

	/// <summary>
	/// Returns the maximum MessagePack serialized size for a key type.
	/// </summary>
	protected static int EstimateMaxKeySize<TKey>() {
		var type = typeof(TKey);

		// integers
		if (type == typeof(byte) || type == typeof(sbyte))
			return 2; // format code + 1 byte
		if (type == typeof(short) || type == typeof(ushort) || type == typeof(char))
			return 3; // format code + 2 bytes
		if (type == typeof(int) || type == typeof(uint))
			return 5; // format code + 4 bytes
		if (type == typeof(long) || type == typeof(ulong))
			return 9; // format code + 8 bytes

		// floating point
		if (type == typeof(float))
			return 5; // format code + 4 bytes
		if (type == typeof(double))
			return 9; // format code + 8 bytes

		// other primitives
		if (type == typeof(bool))
			return 1;
		if (type == typeof(DateTime))
			return 15; // ext format with 8-byte timestamp
		if (type == typeof(DateTimeOffset))
			return 19; // array header + DateTime + int16 offset
		if (type == typeof(TimeSpan))
			return 9; // format code + int64 ticks
		if (type == typeof(decimal))
			return 30; // utf8 string representation

		// common reference/struct key types
		if (type == typeof(Guid))
			return 38; // str8 header (2) + 36 UTF-8 hex chars
		if (type == typeof(string))
			return 38; // assumes Guid.ToString() length: str8 header (2) + 36 UTF-8 chars

		// unmanaged value types: raw size + msgpack header overhead
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>() is false)
			return Unsafe.SizeOf<TKey>() + 5;

		// reference types: conservative estimate
		return 64;
	}

	public abstract KafkaDataCacheStatistics Statistics { get; }
	internal KafkaCachesConsumerStatistics? ConsumerStatistics { get; private protected set; }
	internal void SetConsumerStatistics(KafkaCachesConsumerStatistics consumerStatistics) =>
		ConsumerStatistics = consumerStatistics;
	public abstract string Name { get; }

	public abstract ValueTask HandleAsync(ConsumeResult<RentedBytesWithHandler, RentedBytes> result);
	public abstract ValueTask ExecuteAfterHandlersFilter();


	public abstract Task StartAsync(AsyncCountdownEvent countdownEvent, CancellationToken ct);

	public abstract Task WaitForCompletionAsync();

	internal long HighWatermarkOffset { get; private protected set; }
	internal bool IsInitialConsumeDone { get; set;}
	internal abstract void SetHighWatermarkOffset(long offset);

	internal void UpdateTopicStats(LibrdkafkaTopicStats topicStats) {
		if (topicStats.Partitions is null)
			return;

		long rxMsgs = 0, rxBytes = 0;
		var first = true;
		foreach (var partitionStats in topicStats.Partitions.Values) {
			rxMsgs += partitionStats.RxMsgs;
			rxBytes += partitionStats.RxBytes;

			if (first) {
				Statistics.FetchQueueCount = partitionStats.FetchQueueCount;
				Statistics.FetchQueueSize = partitionStats.FetchQueueSize;
				Statistics.FetchState = partitionStats.FetchState switch {
					"active" => FetchState.Active,
					"none" => FetchState.None,
					"stopping" => FetchState.Stopping,
					"stopped" => FetchState.Stopped,
					"offset-query" => FetchState.OffsetQuery,
					"offset-wait" => FetchState.OffsetWait,
					_ => FetchState.None
				};
				first = false;
			}
		}

		Statistics.TotalMessagesReceived = rxMsgs;
		Statistics.TotalBytesReceived = rxBytes;
	}
}

internal class KafkaCacheHandler<TCacheEntity, TKey, TVlaue> : KafkaCacheHandler
	where TKey : IEquatable<TKey>
	where TVlaue : class, IDataCacheItem<TKey, TVlaue>, IEnrichable<TVlaue>, ICacheEquatable<TVlaue>, IPragueMetadataSettable,
	ICacheClonable<TVlaue>
	where TCacheEntity : IDataCache<TKey, TVlaue> {
	private const int SENSIBLE_CAPACITY = 50;
	private const int COMPACTING_BUFFER_CAPACITY = 50;
	private const int MAX_KEY_RING_BUFFER_SIZE = 8192;

	// 1 (being deserialized by consumer thread) + channel capacity + 1 (being processed by channel loop)
	private static readonly int _keyRingBufferCapacity = (1 + SENSIBLE_CAPACITY + 1) * EstimateMaxKeySize<TKey>() + 1;
	private readonly ICacheAfterHandler<TKey, TVlaue>[] _afterHandlers;
	private readonly TCacheEntity _cache;

	private readonly Channel<ConsumeResult<RentedBytesWithHandler, RentedBytes>> _channel;
	private readonly Enricher<TVlaue> _enricher;
	private readonly KafkaKeyFilters<TKey> _keyFilters;
	private readonly KafkaValueFilters<TVlaue> _valueFilters;
	private readonly ILogger _logger;
	private readonly KafkaDataCacheStatistics _statistics;

	private Task? _channelLoopTask;
	private bool _isLoading = true;
	private long _startTimestamp;

	public KafkaCacheHandler(
		TCacheEntity cache,
		KafkaDataCacheStatistics statistics,
		KafkaHeaderFilters filtes,
		KafkaKeyFilters<TKey> keyFilters,
		KafkaValueFilters<TVlaue> valueFilters,
		IEnumerable<ICacheAfterHandler<TKey, TVlaue>> afterHandlers,
		ILogger logger) : base(filtes, Math.Min(_keyRingBufferCapacity, MAX_KEY_RING_BUFFER_SIZE)) {

		if (_keyRingBufferCapacity > MAX_KEY_RING_BUFFER_SIZE)
			logger.KeyRingBufferExceedsMax(typeof(TCacheEntity).Name, _keyRingBufferCapacity, MAX_KEY_RING_BUFFER_SIZE);

		_cache = cache;
		_statistics = statistics;
		_keyFilters = keyFilters;
		_valueFilters = valueFilters;
		_logger = logger;
		_enricher = TVlaue.GetEnricher();

		_afterHandlers = afterHandlers.ToArray();

		_channel = Channel.CreateBounded<ConsumeResult<RentedBytesWithHandler, RentedBytes>>(
			new BoundedChannelOptions(SENSIBLE_CAPACITY) {
				SingleReader = true,
				SingleWriter = true
			});
	}

	public override KafkaDataCacheStatistics Statistics => _statistics;

	public override string Name => typeof(TCacheEntity).Name;

	public override ValueTask HandleAsync(ConsumeResult<RentedBytesWithHandler, RentedBytes> result) {
		return _channel.Writer.WriteAsync(result);
	}

	public override Task StartAsync(AsyncCountdownEvent countdownEvent, CancellationToken ct) {
		_channelLoopTask = ChannelLoop(countdownEvent, ct);
		return _channelLoopTask.IsCompleted ? _channelLoopTask : Task.CompletedTask;
	}

	public override Task WaitForCompletionAsync() {
		return _channelLoopTask ?? Task.CompletedTask;
	}

	internal override void SetHighWatermarkOffset(long offset) {
		_startTimestamp = Stopwatch.GetTimestamp();
		HighWatermarkOffset = offset;
	}

	private ValueTask HandleUpdate(bool isLoading, TKey key, TVlaue value, Headers headers, Timestamp timestamp, long offset) {
		if (value is null)
			throw new Exception(); // arg

		// Enrich the entity with Kafka metadata using the type-specific enricher
		value.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
		_enricher.Enrich(value, headers, timestamp);

		var updateResult = _cache.AddOrUpdate(value, timestamp.UnixTimestampMs, out var old);

		if (isLoading)
			return ValueTask.CompletedTask;

		return (updateResult, old) switch {
			(false, _) => ExecuteAfterHandlers(UpdateType.Same, key, value, null),
			(_, null) => ExecuteAfterHandlers(UpdateType.Add, key, value, null),
			_ => ExecuteAfterHandlers(UpdateType.Update, key, value, old)
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private async ValueTask ExecuteAfterHandlers(UpdateType type, TKey key, TVlaue? value, TVlaue? old) {
		foreach (var handler in _afterHandlers)
			try {
				await handler.Handle(type, key, value, old);
			}
			catch (Exception e) {
				_logger.AfterHandlerError(e);
			}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public override ValueTask ExecuteAfterHandlersFilter() {
		return _isLoading
			? ValueTask.CompletedTask
			: ExecuteAfterHandlers(UpdateType.Filtered, default!, null, null);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private ValueTask HandleDelete(bool isLoading, Timestamp timestamp, TKey key) {
		if (!_cache.Remove(key, timestamp.UnixTimestampMs, out var value))
			return ValueTask.CompletedTask;
		return isLoading
			? ValueTask.CompletedTask
			: ExecuteAfterHandlers(UpdateType.Delete, key, null, value);
	}


	private void FlushBuffer(CompactingBuffer<TKey> buffer) {
		foreach (var (key, entry) in buffer) {
			try {
				var value = CacheSerde<TVlaue>.Deserialize(entry.Message.Value);
				if (!_valueFilters.IsEmpty) {
					FilterDecision decision;
					try {
						decision = _valueFilters.Evaluate(value);
					} catch (Exception e) {
						_logger.ValueFilterError(e, Name, entry.Offset);
						decision = FilterDecision.Skip;
					}
					if (decision != FilterDecision.Accept) {
						// treatAsDelete: ensure the key is absent after load (an earlier flush may have
						// added it). No after-handler fires during the initial load — mirrors HandleDelete.
						if (decision == FilterDecision.Delete)
							_cache.Remove(key, entry.Message.Timestamp.UnixTimestampMs, out _);
						continue;
					}
				}
				value.SetPragueMetadata(entry.Message.Timestamp.UnixTimestampMs, entry.Offset);
				_enricher.Enrich(value, entry.Message.Headers, entry.Message.Timestamp);
				_cache.AddOrUpdate(value, entry.Message.Timestamp.UnixTimestampMs);
			} catch (Exception e) {
				_logger.ErrorProcessingMessageDuringLoad(e, Name, entry.Offset);
			} finally {
				entry.Message.Value.Dispose();
			}
		}

		buffer.Clear();
	}

	private async Task ChannelLoop(AsyncCountdownEvent countdownEvent, CancellationToken ct) {
		var reader = _channel.Reader;
		var isLoading = _isLoading;
		CompactingBuffer<TKey>? buffer = new(COMPACTING_BUFFER_CAPACITY);
		try {
			while (await reader.WaitToReadAsync(ct))
			while (reader.TryRead(out var result)) {
				_statistics.LastProcessingStartTimestamp = Stopwatch.GetTimestamp();
				try {
					if (result.IsPartitionEOF) {
						if (isLoading) {
							FlushBuffer(buffer!);
							buffer = null;

							var loadTime = Stopwatch.GetElapsedTime(_startTimestamp);
							_logger.CacheLoaded(Name, result.Offset, loadTime.TotalMilliseconds);
							_statistics.SetInitialLoad(loadTime);
							countdownEvent.Signal(loadTime);
							isLoading = false;
							_isLoading = false;
						}

						continue;
					}

					TKey key;
					try {
						key = CacheSerde<TKey>.Deserialize(result.Message.Key);
					} catch (Exception e) {
						_logger.ErrorDeserializingKey(e, Name, result.Offset);
						result.Message.Key.Dispose();
						result.Message.Value.Dispose();
						continue;
					}
					result.Message.Key.Dispose();

					if (!_keyFilters.IsEmpty) {
						FilterDecision decision;
						try {
							decision = _keyFilters.Evaluate(key);
						} catch (Exception e) {
							_logger.KeyFilterError(e, Name, result.Offset);
							decision = FilterDecision.Skip;
						}
						if (decision != FilterDecision.Accept) {
							result.Message.Value.Dispose();
							if (!isLoading) {
								if (decision == FilterDecision.Delete)
									await HandleDelete(isLoading, result.Message.Timestamp, key);
								else
									await ExecuteAfterHandlersFilter();
							} else if (decision == FilterDecision.Delete) {
								// treatAsDelete: ensure the key is absent after load (consistent with the
								// value-filter load path). No after-handler fires during the initial load.
								_cache.Remove(key, result.Message.Timestamp.UnixTimestampMs, out _);
							}
							continue;
						}
					}

					if (isLoading) {
						buffer!.Add(key, result);
						if (buffer.IsFull)
							FlushBuffer(buffer);
						continue;
					}

					try {
						if (result.Message.Value.IsNull) {
							await HandleDelete(isLoading, result.Message.Timestamp, key);
						} else {
							var value = CacheSerde<TVlaue>.Deserialize(result.Message.Value);
							if (!_valueFilters.IsEmpty) {
								FilterDecision decision;
								try {
									decision = _valueFilters.Evaluate(value);
								} catch (Exception e) {
									_logger.ValueFilterError(e, Name, result.Offset);
									decision = FilterDecision.Skip;
								}
								if (decision != FilterDecision.Accept) {
									if (decision == FilterDecision.Delete)
										await HandleDelete(isLoading, result.Message.Timestamp, key);
									else
										await ExecuteAfterHandlersFilter();
									continue;
								}
							}
							await HandleUpdate(isLoading, key, value,
								result.Message.Headers,
								result.Message.Timestamp,
								result.Offset);
						}
					} catch (Exception e) {
						_logger.ErrorProcessingMessage(e, Name, result.Offset);
					}
					finally {
						result.Message.Value.Dispose();
					}
				}
				finally {
					_statistics.LastProcessingStartTimestamp = 0;
				}
			}
		}
		catch (OperationCanceledException) {
			// do nothing - don't throw like a wild man!
		}
		catch (Exception e) {
			_statistics.IsLoopFaulted = true;
			if (ConsumerStatistics is not null)
				ConsumerStatistics.IsFatalLatched = true;
			_logger.ChannelConsumptionError(e, Name);
		}
		finally {
			if (isLoading)
				buffer?.DisposeEntries();
		}
	}
}

internal class KafkaCacheConsumer {
	private readonly IConsumer<RentedBytesWithHandler, RentedBytes> _consumer;
	private readonly CancellationTokenSource _cts = new();

	private readonly FrozenDictionary<string, KafkaCacheHandler> _handlers;
	private readonly ILogger<KafkaCacheConsumer> _logger;

	private readonly AsyncCountdownEvent _manualReset;

	private readonly KafkaCachesConsumerStatistics _statistics;
	private readonly string[] _topics;

	private int _assignedPartitions;
	private int _cachesLoading;
	private Task? _channelLoopTask;

	public KafkaCacheConsumer(
		IKafkaCacheBuilderProvider kafkaCacheBuilderProvider,
		KafkaCachesGlobalOptions globalOptions,
		KafkaCachesOptions configuration,
		KafkaCacheHandlers kafkaCacheHandlers,
		KafkaCachesConsumerStatistics statistics,
		ILogger<KafkaCacheConsumer> logger) {
		_statistics = statistics;
		_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();
		_logger = logger;
		_handlers = kafkaCacheHandlers.Handlers;
		foreach (var handler in _handlers.Values)
			handler.SetConsumerStatistics(_statistics);
		_topics = _handlers.Keys.ToArray();
		_manualReset = new AsyncCountdownEvent(_handlers.Count, statistics);

		var keySerDe = new HeadersFilteringWithHandlerRentedBytesDeserializer(
			KafkaCaches.ProducerInstanceIdHeaderName, KafkaCaches.InstanceIdBytes, _handlers);
		var valueSerDe = new RentedBytesConnectedDeserializer(keySerDe);

		var builder = kafkaCacheBuilderProvider.NewConsumerBuilder<RentedBytesWithHandler, RentedBytes>(
				new ConsumerConfig(configuration.ClientSettings.ToDictionary(kv => kv.Key, kv => kv.Value)) {
					BootstrapServers = configuration.BootstrapServers,
					PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky, // better safe then sorry
					// TODO: add sensible conifg for fast consume, minumum latency
					AllowAutoCreateTopics = false,
					EnablePartitionEof = true,
					GroupId = KafkaCaches.InstanceId.ToString(),
					AutoOffsetReset = AutoOffsetReset.Earliest,
					EnableAutoCommit = false,
					EnableAutoOffsetStore = false,
					FetchWaitMaxMs = 250,
					StatisticsIntervalMs =
						globalOptions is { StatisticsEnabled: true, StatisticsIntervalSeconds: > 1 }
							? (int)TimeSpan.FromSeconds(globalOptions.StatisticsIntervalSeconds).TotalMilliseconds
							: 0
				})
			.SetPartitionsAssignedHandler((c, partitions) => {
				_assignedPartitions += partitions.Count;
				_statistics.SetAssignedPartitions(_assignedPartitions);
				_statistics.HasLostPartitions = false;
				foreach (var partition in partitions) {
					_logger.AssignedToTopic(partition.Topic);
					var watermaker = c.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10));
					if (watermaker is null) {
						_logger.NullWatermark(partition.Topic);
						throw new Exception("Kafka returned null watermark for topic: {partition.Topic}");
					}

					if (!_handlers.TryGetValue(partition.Topic, out var handler))
						continue;
					handler.SetHighWatermarkOffset(watermaker.High.Value);
					_cachesLoading++;
					_statistics.Caches[partition.Topic].AssignedPartitionCount++;
				}
				_statistics.SetCachesLoadingCount(_cachesLoading);
			})
			.SetPartitionsRevokedHandler((_, partitions) => {
				_assignedPartitions -= partitions.Count;
				_statistics.SetAssignedPartitions(_assignedPartitions);
				foreach (var partition in partitions) {
					if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
						cacheStats.AssignedPartitionCount--;
				}
			})
			.SetPartitionsLostHandler((_, partitions) => {
				_assignedPartitions -= partitions.Count;
				_statistics.SetAssignedPartitions(_assignedPartitions);
				_statistics.HasLostPartitions = true;
				foreach (var partition in partitions) {
					if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
						cacheStats.AssignedPartitionCount--;
				}
			})
			.SetStatisticsHandler((_, s) => {
				_statistics.TakeCachesSnapshot(DateTimeOffset.UtcNow);
				var snapshot = System.Text.Json.JsonSerializer.Deserialize(s, LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);
				_statistics.UpdateFromLibrdkafkaStats(snapshot);
				if (snapshot.Topics is not null)
					foreach (var (topicName, topicStats) in snapshot.Topics)
						if (_handlers.TryGetValue(topicName, out var handler))
							handler.UpdateTopicStats(topicStats);
			})
			.SetKeyDeserializer(keySerDe)
			.SetValueDeserializer(valueSerDe)
			.SetErrorHandler((c, e) => { _logger.ConsumerError(e.Reason); });
		// TODO: add logging and error handling

		_consumer = builder.Build();
	}

	public Task WaitForInitialLoadAsync() {
		return _manualReset.WaitAsync();
	}

	internal async Task ExecuteAsync(CancellationToken ct) {
		var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
		foreach (var kafkaCacheHandler in _handlers.Values)
			await kafkaCacheHandler.StartAsync(_manualReset, cts.Token);

		_channelLoopTask = Consume(_consumer, cts.Token);
		// force exceptions
		if (_channelLoopTask.IsCompleted)
			await _channelLoopTask;
	}

	internal Task WaitForCompletionAsync() {
		_cts.Cancel();
		Task.WhenAll(_handlers.Values.Select(h => h.WaitForCompletionAsync()));
		return _channelLoopTask ?? Task.CompletedTask;
	}

	private async Task Consume(IConsumer<RentedBytesWithHandler, RentedBytes> consumer, CancellationToken ct) {
		try {
			consumer.Subscribe(_topics);
			await Task.Yield();
			var loadingCount = _topics.Length;
			while (!ct.IsCancellationRequested)
				try {
					var consumeResult = consumer.Consume(100);
					_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();

					if (ct.IsCancellationRequested || consumeResult is null)
						continue;

					Debug.Assert(consumeResult is not null, "librdkafka API guarantees consumerResult is not null");

					if (consumeResult.IsPartitionEOF) {
						// we've loaded them all,
						// no handler, handler already received right
						// offset is too early
						if (loadingCount == 0
						    || !_handlers.TryGetValue(consumeResult.TopicPartition.Topic, out var handler)
						    || handler.IsInitialConsumeDone
						    || consumeResult.Offset < handler.HighWatermarkOffset)
						    continue;

						loadingCount--;
						_cachesLoading--;
						_statistics.SetCachesLoadingCount(_cachesLoading);
						handler.IsInitialConsumeDone = true;
						await handler.HandleAsync(consumeResult);
						continue;
					}

					Debug.Assert(consumeResult.Message is not null, "Message is not null unless partitionEOF");

					if (consumeResult.Message.Key.IsFiltered) {
						var handler = consumeResult.Message.Key.Handler;
						if (handler is not null)
							await handler.ExecuteAfterHandlersFilter();
						continue;
					}

					Debug.Assert(consumeResult.Message.Key.Handler is not null, "No handler should be treated as filtered");

					await consumeResult.Message.Key.Handler.HandleAsync(consumeResult);
				}
				catch (OperationCanceledException) {
					_manualReset.TrySetCanceled();
					break;
				}
				catch (KafkaException e) when (e.Error.IsFatal) {
					_statistics.IsFatalLatched = true;
					_logger.FatalKafkaError(e);
					_manualReset.TrySetException(e);
					throw;
				}
				catch (ConsumeException e) when (e.Error.Code is ErrorCode.Local_ValueDeserialization) {
					_logger.DeserializationError(e, e.ConsumerRecord.Topic, e.ConsumerRecord.Offset);
				}
				catch (KafkaException e) when (e.Error.IsFatal is false) {
					if (KafkaErrorHandling.IsErrorFatal.TryGetValue(e.Error.Code, out var isAppFatal) && isAppFatal) {
						_statistics.IsFatalLatched = true;
						_logger.AppFatalKafkaError(e, e.Error.Code.ToString());
						_manualReset.TrySetException(e);
						throw;
					}
					_logger.NonFatalKafkaError(e);
				}
				catch (Exception e) {
					_statistics.IsFatalLatched = true;
					// TODO: Handle manualReset!!!
					_logger.UnexpectedError(e);
					// fail fast!
					throw;
				}
		}
		catch (OperationCanceledException) {
			_manualReset.TrySetCanceled();
		} catch (Exception ex) {
			_statistics.IsFatalLatched = true;
			_manualReset.TrySetException(ex);
			// Swallow exception during shutdown
		}
		finally {
			consumer.Dispose();
		}
	}
}

internal static partial class KafkaCacheConsumerLog {

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "[Prague] Key ring buffer for {CacheName} calculated size {CalculatedSize} exceeds max {MaxSize}, large keys will fall back to heap allocation")]
	public static partial void KeyRingBufferExceedsMax(this ILogger logger, string cacheName, int calculatedSize, int maxSize);

	[LoggerMessage(Level = LogLevel.Error, Message = "Error executing after handler")]
	public static partial void AfterHandlerError(this ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Error processing message during load {CacheName} - {Offset}")]
	public static partial void ErrorProcessingMessageDuringLoad(this ILogger logger, Exception exception, string cacheName, long offset);

	[LoggerMessage(Level = LogLevel.Information,
		Message = "[Prague] Kafka Cache {CacheName} loaded to offset {Offset} in {LoadTimeMs} ms")]
	public static partial void CacheLoaded(this ILogger logger, string cacheName, long offset, double loadTimeMs);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Error deserializing key {CacheName} - {Offset}")]
	public static partial void ErrorDeserializingKey(this ILogger logger, Exception exception, string cacheName, long offset);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Key filter predicate threw for {CacheName} - {Offset}")]
	public static partial void KeyFilterError(this ILogger logger, Exception exception, string cacheName, long offset);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Value filter predicate threw for {CacheName} - {Offset}")]
	public static partial void ValueFilterError(this ILogger logger, Exception exception, string cacheName, long offset);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Error processing message {CacheName} - {Offset}")]
	public static partial void ErrorProcessingMessage(this ILogger logger, Exception exception, string cacheName, long offset);

	[LoggerMessage(Level = LogLevel.Critical,
		Message = "[Prague] {Handler} channel consumption error")]
	public static partial void ChannelConsumptionError(this ILogger logger, Exception exception, string handler);

	// KafkaCacheConsumer logs

	[LoggerMessage(Level = LogLevel.Information,
		Message = "[Prague] assigned to topic {Topic} - starting cache load")]
	public static partial void AssignedToTopic(this ILogger logger, string topic);

	[LoggerMessage(Level = LogLevel.Critical,
		Message = "[Prague] Kafka returned null watermark for topic: {Topic}")]
	public static partial void NullWatermark(this ILogger logger, string topic);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "Kafka caches consumer error: {Error}")]
	public static partial void ConsumerError(this ILogger logger, string error);

	[LoggerMessage(Level = LogLevel.Critical,
		Message = "Fatal error occured in Kafka Caches Consumer")]
	public static partial void FatalKafkaError(this ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "[Prague] Error deserializing to bytes {Topic} - {Offset}")]
	public static partial void DeserializationError(this ILogger logger, Exception exception, string topic, long offset);

	[LoggerMessage(Level = LogLevel.Critical,
		Message = "[Prague] App-fatal Kafka error: {Code}")]
	public static partial void AppFatalKafkaError(this ILogger logger, Exception exception, string code);

	[LoggerMessage(Level = LogLevel.Warning,
		Message = "[Prague] Non-fatal error occured in Kafka Caches Consumer")]
	public static partial void NonFatalKafkaError(this ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Error,
		Message = "[Prague] Unexpected error occured in Kafka Caches Consumer")]
	public static partial void UnexpectedError(this ILogger logger, Exception exception);
}

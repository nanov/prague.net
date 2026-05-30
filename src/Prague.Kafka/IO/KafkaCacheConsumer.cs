namespace Prague.Kafka.IO;

using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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

	public KafkaCacheHandler(KafkaHeaderFilters headersFilters) {
		HeadersFilters = headersFilters;
	}

	public abstract KafkaDataCacheStatistics Statistics { get; }
	internal KafkaCachesConsumerStatistics? ConsumerStatistics { get; private protected set; }
	internal void SetConsumerStatistics(KafkaCachesConsumerStatistics consumerStatistics) =>
		ConsumerStatistics = consumerStatistics;
	public abstract string Name { get; }

	public abstract Task WaitForCompletionAsync();

	// --- Raw consume path ---

	/// <summary>
	///   Process one raw message. During loading it deserializes + enriches off the native spans and
	///   compacts the materialized value by key; live it publishes the materialized value to the
	///   per-handler ring-buffer worker. All span reads complete before the caller disposes the message.
	/// </summary>
	internal abstract void DispatchRaw(in RawMessage raw, bool isLoading);

	/// <summary>Live-phase only: fire the <c>Filtered</c> after-handler (header-filtered message).</summary>
	internal abstract void PublishRawFiltered();

	/// <summary>At partition EOF: flush the object-compaction buffer to the cache, signal load complete, go live.</summary>
	internal abstract void FlushRawLoadBufferAndGoLive(AsyncCountdownEvent countdownEvent, long offset, CancellationToken ct);

	/// <summary>Tear down the live worker on shutdown.</summary>
	internal abstract void StopRawWorker();

	/// <summary>
	///   Span-based header filtering for the raw path — producer-instance self-filter plus the handler's
	///   configured header filters, evaluated against UTF-8 name/value spans with no allocation.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool IsHeaderFiltered(in RawHeaders headers) {
		var filters = HeadersFilters;
		var state = filters.InitialState;
		foreach (var (name, value) in headers) {
			if (System.Text.Ascii.Equals(name, KafkaCaches.ProducerInstanceIdHeaderName)
			    && value.SequenceEqual(KafkaCaches.InstanceIdBytes))
				return true;
			if (!filters.ShouldProcess(ref state, name, value))
				return true;
		}

		return !state;
	}

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
	private const int COMPACTING_BUFFER_CAPACITY = 50;

	private readonly ICacheAfterHandler<TKey, TVlaue>[] _afterHandlers;
	private readonly TCacheEntity _cache;
	private readonly Enricher<TVlaue> _enricher;
	private readonly KafkaKeyFilters<TKey> _keyFilters;
	private readonly KafkaValueFilters<TVlaue> _valueFilters;
	private readonly ILogger _logger;
	private readonly KafkaDataCacheStatistics _statistics;

	private bool _isLoading = true;
	private long _startTimestamp;

	public KafkaCacheHandler(
		TCacheEntity cache,
		KafkaDataCacheStatistics statistics,
		KafkaHeaderFilters filtes,
		KafkaKeyFilters<TKey> keyFilters,
		KafkaValueFilters<TVlaue> valueFilters,
		IEnumerable<ICacheAfterHandler<TKey, TVlaue>> afterHandlers,
		ILogger logger) : base(filtes) {
		_cache = cache;
		_statistics = statistics;
		_keyFilters = keyFilters;
		_valueFilters = valueFilters;
		_logger = logger;
		_enricher = TVlaue.GetEnricher();
		_afterHandlers = afterHandlers.ToArray();
	}

	public override KafkaDataCacheStatistics Statistics => _statistics;

	public override string Name => typeof(TCacheEntity).Name;

	public override Task WaitForCompletionAsync() {
		return _rawWorker?.Completion ?? Task.CompletedTask;
	}

	private const byte RAW_KIND_UPDATE = 0;
	private const byte RAW_KIND_DELETE = 1;
	private const byte RAW_KIND_FILTERED = 2;
	private const int RAW_WORKER_CAPACITY = 64;

	private RawLiveWorker? _rawWorker;
	private ValueCompactingBuffer<TKey, TVlaue>? _rawLoadBuffer;

	internal override void DispatchRaw(in RawMessage raw, bool isLoading) {
		var offset = raw.Offset.Value;
		TKey key;
		try {
			key = CacheSerde<TKey>.DeserializeFromSpan(raw.Key);
		} catch (Exception e) {
			_logger.ErrorDeserializingKey(e, Name, offset);
			return;
		}

		if (!_keyFilters.IsEmpty) {
			FilterDecision decision;
			try {
				decision = _keyFilters.Evaluate(key);
			} catch (Exception e) {
				_logger.KeyFilterError(e, Name, offset);
				decision = FilterDecision.Skip;
			}
			if (decision != FilterDecision.Accept) {
				if (isLoading) {
					if (decision == FilterDecision.Delete)
						_rawLoadBuffer?.Remove(key);
				} else if (decision == FilterDecision.Delete) {
					PublishRaw(RAW_KIND_DELETE, key, null, raw.Timestamp.UnixTimestampMs);
				} else {
					PublishRaw(RAW_KIND_FILTERED, default!, null, 0);
				}

				return;
			}
		}

		var timestamp = raw.Timestamp;
		var valueSpan = raw.Value;

		if (isLoading) {
			// Empty value span == tombstone — cancel any pending buffered value for this key.
			if (valueSpan.IsEmpty) {
				_rawLoadBuffer?.Remove(key);
				return;
			}

			TVlaue value;
			try {
				value = CacheSerde<TVlaue>.DeserializeFromSpan(valueSpan);
			} catch (Exception e) {
				_logger.ErrorProcessingMessageDuringLoad(e, Name, offset);
				return;
			}

			if (!_valueFilters.IsEmpty) {
				FilterDecision decision;
				try {
					decision = _valueFilters.Evaluate(value);
				} catch (Exception e) {
					_logger.ValueFilterError(e, Name, offset);
					decision = FilterDecision.Skip;
				}
				if (decision != FilterDecision.Accept) {
					if (decision == FilterDecision.Delete)
						_rawLoadBuffer?.Remove(key);
					return;
				}
			}

			value.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
			_enricher.Enrich(value, raw.Headers, timestamp);
			var buffer = _rawLoadBuffer ??= new ValueCompactingBuffer<TKey, TVlaue>(COMPACTING_BUFFER_CAPACITY);
			buffer.AddOrReplace(key, value, timestamp.UnixTimestampMs);
			if (buffer.IsFull(COMPACTING_BUFFER_CAPACITY))
				FlushRawLoadBufferToCache();
			return;
		}

		// live phase
		if (valueSpan.IsEmpty) {
			PublishRaw(RAW_KIND_DELETE, key, null, timestamp.UnixTimestampMs);
			return;
		}

		TVlaue liveValue;
		try {
			liveValue = CacheSerde<TVlaue>.DeserializeFromSpan(valueSpan);
		} catch (Exception e) {
			_logger.ErrorProcessingMessage(e, Name, offset);
			return;
		}

		if (!_valueFilters.IsEmpty) {
			FilterDecision decision;
			try {
				decision = _valueFilters.Evaluate(liveValue);
			} catch (Exception e) {
				_logger.ValueFilterError(e, Name, offset);
				decision = FilterDecision.Skip;
			}
			if (decision != FilterDecision.Accept) {
				PublishRaw(decision == FilterDecision.Delete ? RAW_KIND_DELETE : RAW_KIND_FILTERED,
					key, null, timestamp.UnixTimestampMs);
				return;
			}
		}

		liveValue.SetPragueMetadata(timestamp.UnixTimestampMs, offset);
		_enricher.Enrich(liveValue, raw.Headers, timestamp);
		PublishRaw(RAW_KIND_UPDATE, key, liveValue, timestamp.UnixTimestampMs);
	}

	internal override void PublishRawFiltered()
		=> PublishRaw(RAW_KIND_FILTERED, default!, null, 0);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void PublishRaw(byte kind, TKey key, TVlaue? value, long timestampMs) {
		var worker = _rawWorker;
		if (worker is null)
			return;
		using var scope = worker.Publish();
		if (!scope.IsOpen)
			return;
		ref var slot = ref scope.Event();
		slot.Kind = kind;
		slot.Key = key;
		slot.Value = value;
		slot.TimestampMs = timestampMs;
	}

	private void FlushRawLoadBufferToCache() {
		if (_rawLoadBuffer is null)
			return;
		foreach (var (value, ts) in _rawLoadBuffer)
			try {
				_cache.AddOrUpdate(value, ts);
			} catch (Exception e) {
				_logger.ErrorProcessingMessageDuringLoad(e, Name, 0);
			}

		_rawLoadBuffer.Clear();
	}

	internal override void FlushRawLoadBufferAndGoLive(AsyncCountdownEvent countdownEvent, long offset, CancellationToken ct) {
		var loadTime = Stopwatch.GetElapsedTime(_startTimestamp);
		FlushRawLoadBufferToCache();
		_rawLoadBuffer = null;
		_logger.CacheLoaded(Name, offset, loadTime.TotalMilliseconds);
		_statistics.SetInitialLoad(loadTime);
		countdownEvent.Signal(loadTime);
		_isLoading = false;
		_rawWorker = new RawLiveWorker(this, RAW_WORKER_CAPACITY);
		_rawWorker.Start(ct);
	}

	internal override void StopRawWorker()
		=> _rawWorker?.Dispose();

	private ValueTask ApplyRawLiveAsync(byte kind, TKey key, TVlaue? value, long timestampMs)
		=> kind switch {
			RAW_KIND_UPDATE => HandleRawLiveUpdate(key, value!, timestampMs),
			RAW_KIND_DELETE => HandleRawLiveDelete(key, timestampMs),
			_ => ExecuteAfterHandlers(UpdateType.Filtered, default!, null, null)
		};

	private ValueTask HandleRawLiveUpdate(TKey key, TVlaue value, long timestampMs) {
		var updateResult = _cache.AddOrUpdate(value, timestampMs, out var old);
		return (updateResult, old) switch {
			(false, _) => ExecuteAfterHandlers(UpdateType.Same, key, value, null),
			(_, null) => ExecuteAfterHandlers(UpdateType.Add, key, value, null),
			_ => ExecuteAfterHandlers(UpdateType.Update, key, value, old)
		};
	}

	private ValueTask HandleRawLiveDelete(TKey key, long timestampMs) {
		if (!_cache.Remove(key, timestampMs, out var old))
			return ValueTask.CompletedTask;
		return ExecuteAfterHandlers(UpdateType.Delete, key, null, old);
	}

	private struct RawWorkItem {
		public byte Kind;
		public TKey Key;
		public TVlaue? Value;
		public long TimestampMs;
	}

	private sealed class RawLiveWorker : AsyncValueBufferedWorker<RawWorkItem> {
		private readonly KafkaCacheHandler<TCacheEntity, TKey, TVlaue> _owner;

		public RawLiveWorker(KafkaCacheHandler<TCacheEntity, TKey, TVlaue> owner, int capacity)
			: base(capacity, $"PragueRawWorker<{typeof(TCacheEntity).Name}>") {
			_owner = owner;
		}

		protected override ValueTask ProcessAsync(ref ConsumeScope<RawWorkItem> scope, CancellationToken cancellationToken) {
			ref var slot = ref scope.Event();
			var kind = slot.Kind;
			var key = slot.Key;
			var value = slot.Value;
			var ts = slot.TimestampMs;
			scope.Release();
			return _owner.ApplyRawLiveAsync(kind, key, value, ts);
		}
	}

	internal override void SetHighWatermarkOffset(long offset) {
		_startTimestamp = Stopwatch.GetTimestamp();
		HighWatermarkOffset = offset;
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

}

internal class KafkaCacheConsumer {
	private readonly IRawConsumer _rawConsumer;
	private readonly FrozenDictionary<string, KafkaCacheHandler>.AlternateLookup<ReadOnlySpan<char>> _handlersByName;
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

		var config = new ConsumerConfig(configuration.ClientSettings.ToDictionary(kv => kv.Key, kv => kv.Value)) {
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
		};

		_handlersByName = _handlers
			.ToFrozenDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
			.GetAlternateLookup<ReadOnlySpan<char>>();
		_rawConsumer = BuildRawConsumer(kafkaCacheBuilderProvider, config);
	}

	public Task WaitForInitialLoadAsync() {
		return _manualReset.WaitAsync();
	}

	internal Task ExecuteAsync(CancellationToken ct) {
		var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
		// Dedicated thread — the raw loop is fully synchronous per message (no per-message await);
		// per-handler ring-buffer workers start at each cache's partition EOF.
		_channelLoopTask = Task.Factory.StartNew(
			() => ConsumeRawLoop(_rawConsumer, cts.Token),
			cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		return Task.CompletedTask;
	}

	internal Task WaitForCompletionAsync() {
		_cts.Cancel();
		Task.WhenAll(_handlers.Values.Select(h => h.WaitForCompletionAsync()));
		return _channelLoopTask ?? Task.CompletedTask;
	}

	private IRawConsumer BuildRawConsumer(IKafkaCacheBuilderProvider provider, ConsumerConfig config) {
		var rawBuilder = provider.NewRawConsumerBuilder(config);
		rawBuilder.SetPartitionsAssignedHandler((c, partitions) => {
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
		});
		rawBuilder.SetPartitionsRevokedHandler((_, partitions) => {
			_assignedPartitions -= partitions.Count;
			_statistics.SetAssignedPartitions(_assignedPartitions);
			foreach (var partition in partitions)
				if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
					cacheStats.AssignedPartitionCount--;
		});
		rawBuilder.SetPartitionsLostHandler((_, partitions) => {
			_assignedPartitions -= partitions.Count;
			_statistics.SetAssignedPartitions(_assignedPartitions);
			_statistics.HasLostPartitions = true;
			foreach (var partition in partitions)
				if (_statistics.Caches.TryGetValue(partition.Topic, out var cacheStats))
					cacheStats.AssignedPartitionCount--;
		});
		rawBuilder.SetErrorHandler((_, e) => _logger.ConsumerError(e.Reason));
		if (config.StatisticsIntervalMs is > 0)
			rawBuilder.SetStatisticsHandler((ReadOnlySpan<byte> s) => {
				_statistics.TakeCachesSnapshot(DateTimeOffset.UtcNow);
				var snapshot = System.Text.Json.JsonSerializer.Deserialize(s, LibrdkafkaStatsJsonContext.Default.LibrdkafkaStatsSnapshot);
				_statistics.UpdateFromLibrdkafkaStats(snapshot);
				if (snapshot.Topics is not null)
					foreach (var (topicName, topicStats) in snapshot.Topics)
						if (_handlers.TryGetValue(topicName, out var handler))
							handler.UpdateTopicStats(topicStats);
			});

		return rawBuilder.BuildRaw();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private KafkaCacheHandler? ResolveHandler(ReadOnlySpan<byte> topicUtf8, Span<char> scratch) {
		var charCount = System.Text.Encoding.UTF8.GetChars(topicUtf8, scratch);
		return _handlersByName.TryGetValue(scratch[..charCount], out var handler) ? handler : null;
	}

	private void ConsumeRawLoop(IRawConsumer consumer, CancellationToken ct) {
		Span<char> topicScratch = stackalloc char[256];
		try {
			consumer.Subscribe(_topics);
			var loadingCount = _topics.Length;
			while (!ct.IsCancellationRequested)
				try {
					using var raw = consumer.ConsumeRaw(100);
					_statistics.LastPollTimestamp = Stopwatch.GetTimestamp();

					if (ct.IsCancellationRequested || raw.IsEmpty)
						continue;

					var handler = ResolveHandler(raw.Topic, topicScratch);

					if (raw.IsPartitionEOF) {
						if (loadingCount == 0
						    || handler is null
						    || handler.IsInitialConsumeDone
						    || raw.Offset.Value < handler.HighWatermarkOffset)
							continue;

						loadingCount--;
						_cachesLoading--;
						_statistics.SetCachesLoadingCount(_cachesLoading);
						handler.IsInitialConsumeDone = true;
						handler.FlushRawLoadBufferAndGoLive(_manualReset, raw.Offset.Value, ct);
						continue;
					}

					if (handler is null)
						continue;

					if (handler.IsHeaderFiltered(raw.Headers)) {
						if (handler.IsInitialConsumeDone)
							handler.PublishRawFiltered();
						continue;
					}

					handler.DispatchRaw(in raw, !handler.IsInitialConsumeDone);
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
					_logger.UnexpectedError(e);
					throw;
				}
		}
		catch (OperationCanceledException) {
			_manualReset.TrySetCanceled();
		}
		catch (Exception ex) {
			_statistics.IsFatalLatched = true;
			_manualReset.TrySetException(ex);
		}
		finally {
			foreach (var handler in _handlers.Values)
				handler.StopRawWorker();
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

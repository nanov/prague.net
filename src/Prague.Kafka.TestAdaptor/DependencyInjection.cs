namespace Prague.Kafka.TestAdaptor;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class DependencyInjection {
	public static IKafkaCacheTestBuilderProvider AddKafkaCacheTestCluster(this IServiceCollection services) {
		var p = new KafkaCacheTestBuilderProvider();
		KafkaCacheTestBuilderProviderMarshall.AddCacheTopics(p);
		services.TryAddSingleton<IKafkaCacheBuilderProvider>(s => (IKafkaCacheBuilderProvider)p.Init(s));
		return p;
	}
}

public interface IKafkaCacheTestBuilderProvider {
	internal void AddTopics(string[] topics);
	internal void Produce(string topic, ConsumeResult<byte[], byte[]> message);
	internal void SimulatePartitionEof(string topic);
	public IKafkaCacheTestBuilderProvider AddCacheTopics();
	public IKafkaCacheTestBuilderProvider InjectDump(string path);
	public IKafkaCacheTestBuilderProvider InjectDumps(string path);
}

public static partial class KafkaCacheTestBuilderProviderMarshall {
	public static void AddTopics(IKafkaCacheTestBuilderProvider provider, string[] topics) {
		((IKafkaCacheTestBuilderProvider)provider).AddTopics(topics);
	}

	public static void Produce(IKafkaCacheTestBuilderProvider provider, string topic,
		ConsumeResult<byte[], byte[]> message) {
		((IKafkaCacheTestBuilderProvider)provider).Produce(topic, message);
	}

	public static void SimulatePartitionEof(IKafkaCacheTestBuilderProvider provider, string topic) {
		((IKafkaCacheTestBuilderProvider)provider).SimulatePartitionEof(topic);
	}

	static partial void AddCacheTopicsImpl(IKafkaCacheTestBuilderProvider provider);

	public static void AddCacheTopics(IKafkaCacheTestBuilderProvider provider) {
		AddCacheTopicsImpl(provider);
	}
}

public sealed class KafkaCacheTestBuilderProvider : IKafkaCacheBuilderProvider, IKafkaCacheTestBuilderProvider {
	private readonly TestKafkaCluster _cluster = new();
	private readonly List<Action<IKafkaCacheTestBuilderProvider, IServiceProvider>> _initActions = new List<Action<IKafkaCacheTestBuilderProvider, IServiceProvider>>();

	public IKafkaCacheTestBuilderProvider AddCacheTopics() {
		_initActions.Add((b, sp) => {
			var r = sp.GetRequiredService<IDataCacheRegistry>();
			var cs = r.GetCachesAs<IKafkaCache>().Select(c => c.Topic);
			b.AddTopics(cs.ToArray());
		});
		return this;
	}

	public IKafkaCacheTestBuilderProvider InjectDump(string path) {
		var bytes = File.ReadAllBytes(path);
		var records = MessagePack.MessagePackSerializer.Deserialize<List<Dump.PragueConsumeResult>>(bytes, Prague.Kafka.PragueMessagePack.Options);
		if (records.Count == 0) return this;

		var topic = records[0].Topic;
		_cluster.AddTopic(topic);

		foreach (var record in records) {
			var headers = new Headers();
			if (record.Headers is not null)
				foreach (var h in record.Headers)
					headers.Add(h.Key, h.Value);

			_cluster.Produce(topic, new ConsumeResult<byte[], byte[]> {
				Topic = topic,
				Partition = new Partition(record.Partition),
				Offset = new Offset(record.Offset),
				Message = new Message<byte[], byte[]> {
					Key = record.Key,
					Value = record.Value,
					Headers = headers,
					Timestamp = new Timestamp(record.TimestampMs, TimestampType.CreateTime)
				}
			});
		}

		_cluster.SimulatePartitionEof(topic);
		return this;
	}

	public IKafkaCacheTestBuilderProvider InjectDumps(string path) {
		foreach (var file in Directory.GetFiles(path, "*.pkd"))
			InjectDump(file);
		return this;
	}

	public IKafkaCacheTestBuilderProvider Init(IServiceProvider sp) {
		foreach (var action in _initActions)
			action(this, sp);
		return this;
	}

	ConsumerBuilder<TKey, TValue> IKafkaCacheBuilderProvider.NewConsumerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new TestKafkaConsumerBuilder<TKey, TValue>(_cluster, config);
	}

	ProducerBuilder<TKey, TValue> IKafkaCacheBuilderProvider.NewProducerBuilder<TKey, TValue>(
		IEnumerable<KeyValuePair<string, string>> config) {
		return new TestKafkaProducerBuilder<TKey, TValue>(_cluster, config);
	}

	void IKafkaCacheTestBuilderProvider.AddTopics(string[] topics) {
		foreach (var topic in topics)
			_cluster.AddTopic(topic);
	}

	void IKafkaCacheTestBuilderProvider.Produce(string topic, ConsumeResult<byte[], byte[]> message) {
		_cluster.Produce(topic, message);
	}

	void IKafkaCacheTestBuilderProvider.SimulatePartitionEof(string topic) {
		_cluster.SimulatePartitionEof(topic);
	}
}

internal sealed class TestKafkaConsumerBuilder<TKey, TValue> : ConsumerBuilder<TKey, TValue> {
	private readonly TestKafkaCluster _cluster;
	private readonly IEnumerable<KeyValuePair<string, string>> _config;

	public TestKafkaConsumerBuilder(TestKafkaCluster cluster, IEnumerable<KeyValuePair<string, string>> config) :
		base(config) {
		_cluster = cluster;
		_config = config;
	}

	internal IDeserializer<TKey> KafkaKeyDeserializer
		=> KeyDeserializer;

	internal IDeserializer<TValue> KafkaValueDeserializer
		=> ValueDeserializer;

	internal Func<IConsumer<TKey, TValue>, List<TopicPartition>, IEnumerable<TopicPartitionOffset>>? KafkaPartitionsAssignedHandler
		=> PartitionsAssignedHandler;

	internal string? GetGroupId() {
		return _config.FirstOrDefault(kvp => kvp.Key == "group.id").Value;
	}

	public override IConsumer<TKey, TValue> Build() {
		return new TestKafkaConsumer<TKey, TValue>(_cluster, this);
	}
}

internal sealed class TestKafkaProducerBuilder<TKey, TValue> : ProducerBuilder<TKey, TValue> {
	private readonly TestKafkaCluster _cluster;

	public TestKafkaProducerBuilder(TestKafkaCluster cluster, IEnumerable<KeyValuePair<string, string>> config) :
		base(config) {
		_cluster = cluster;
	}

	internal ISerializer<TKey> KafkaKeySerializer
		=> KeySerializer;

	internal ISerializer<TValue> KafkaValueSerializer
		=> ValueSerializer;

	public override IProducer<TKey, TValue> Build() {
		return new TestKafkaProducer<TKey, TValue>(_cluster, this);
	}
}

internal sealed class TestKafkaCluster {
	private readonly Dictionary<string, ConsumerGroup> _consumerGroups = new();
	private readonly Lock _lock = new();
	private readonly Dictionary<string, List<ConsumeResult<byte[], byte[]>>> _topicMessages = new();

	internal void AddTopic(string topic) {
		lock (_lock) {
			if (!_topicMessages.ContainsKey(topic))
				_topicMessages[topic] = new List<ConsumeResult<byte[], byte[]>>();
		}
	}

	internal WatermarkOffsets QueryWatermarkOffsets(string topic) {
		lock (_lock) {
			if (!_topicMessages.TryGetValue(topic, out var messages))
				throw new KafkaException(ErrorCode.UnknownTopicOrPart);
			if (messages.Count == 0)
				return new WatermarkOffsets(0, 0);
			var last = messages[^1];
			return new WatermarkOffsets(0, last.Offset);
		}
	}

	internal ConsumerSubscription AddSubscription(string groupId, IEnumerable<string> topics) {
		var topicsArray = topics.ToArray();

		lock (_lock) {
			// Validate all topics exist
			foreach (var topic in topicsArray)
				if (!_topicMessages.TryGetValue(topic, out _))
					throw new KafkaException(ErrorCode.UnknownTopicOrPart);

			// Get or create consumer group
			if (!_consumerGroups.TryGetValue(groupId, out var group)) {
				group = new ConsumerGroup(groupId);
				_consumerGroups[groupId] = group;
			}

			// Create subscription and add to group
			var sub = new ConsumerSubscription(topicsArray);
			group.AddConsumer(sub);

			// Replay existing messages for subscribed topics
			foreach (var topic in topicsArray)
				if (_topicMessages.TryGetValue(topic, out var messages)) {
					// For a new consumer in an existing group, replay all messages
					// In real Kafka, this would depend on offset management, but for now replay all
					foreach (var message in messages)
						sub.Add(message);

					// Add EOF marker with offset = high watermark (message count)
					sub.Add(new ConsumeResult<byte[], byte[]> {
						Topic = topic,
						Partition = 0,
						Offset = messages.Count,
						IsPartitionEOF = true,
						Message = null
					});
				}

			return sub;
		}
	}

	internal void SimulatePartitionEof(string topic) {
		Dictionary<string, ConsumerGroup> groupsSnapshot;
		long offset;
		lock (_lock) {
			if (!_topicMessages.TryGetValue(topic, out var messages))
				throw new KafkaException(ErrorCode.UnknownTopicOrPart);
			offset = messages.Count;
			groupsSnapshot = new Dictionary<string, ConsumerGroup>(_consumerGroups);
		}

		var eof = new ConsumeResult<byte[], byte[]> {
			Topic = topic,
			Partition = 0,
			Offset = offset,
			IsPartitionEOF = true,
			Message = null
		};

		foreach (var group in groupsSnapshot.Values)
			group.DistributeMessage(eof);
	}

	internal void RemoveSubscription(string groupId, ConsumerSubscription subscription) {
		lock (_lock) {
			if (_consumerGroups.TryGetValue(groupId, out var group)) {
				group.RemoveConsumer(subscription);
				if (group.IsEmpty)
					_consumerGroups.Remove(groupId);
			}
		}
	}

	public void Produce(string topic, ConsumeResult<byte[], byte[]> message) {
		if (message.Message is null)
			throw new InvalidOperationException("Produce message cannot be null");

		Dictionary<string, ConsumerGroup> groupsSnapshot;

		lock (_lock) {
			if (!_topicMessages.TryGetValue(topic, out var messages))
				throw new KafkaException(ErrorCode.UnknownTopicOrPart);

			message.Message.Headers ??= new Headers();
			message.Topic = topic;
			message.Partition = 0;
			message.Offset = messages.Count;
			message.Message.Timestamp = new Timestamp(DateTimeOffset.UtcNow);
			messages.Add(message);

			// Snapshot consumer groups under lock
			groupsSnapshot = new Dictionary<string, ConsumerGroup>(_consumerGroups);
		}

		// Distribute message to each consumer group (outside lock)
		foreach (var group in groupsSnapshot.Values)
			group.DistributeMessage(message);
	}

	/// <summary>
	///   Represents a consumer group where messages are load-balanced among consumers
	/// </summary>
	internal sealed class ConsumerGroup {
		private readonly List<ConsumerSubscription> _consumers = new();
		private readonly string _groupId;
		private readonly Lock _lock = new();
		private int _nextConsumerIndex;

		public ConsumerGroup(string groupId) {
			_groupId = groupId;
		}

		public bool IsEmpty {
			get {
				lock (_lock) {
					return _consumers.Count == 0;
				}
			}
		}

		public void AddConsumer(ConsumerSubscription consumer) {
			lock (_lock) {
				_consumers.Add(consumer);
			}
		}

		public void RemoveConsumer(ConsumerSubscription consumer) {
			lock (_lock) {
				_consumers.Remove(consumer);
			}
		}

		/// <summary>
		///   Distributes a message to ONE consumer in the group (round-robin among consumers interested in the topic)
		/// </summary>
		public void DistributeMessage(ConsumeResult<byte[], byte[]> message) {
			lock (_lock) {
				if (_consumers.Count == 0)
					return;

				// Find all consumers subscribed to this topic
				var interestedConsumers = _consumers
					.Where(c => c.IsSubscribedTo(message.Topic))
					.ToArray();

				if (interestedConsumers.Length == 0)
					return;

				// Round-robin among interested consumers
				var consumerIndex = _nextConsumerIndex % interestedConsumers.Length;
				var targetConsumer = interestedConsumers[consumerIndex];
				targetConsumer.Add(message);

				_nextConsumerIndex = (_nextConsumerIndex + 1) % interestedConsumers.Length;
			}
		}
	}

	internal sealed class ConsumerSubscription : IDisposable {
		private readonly BlockingCollection<ConsumeResult<byte[], byte[]>> _messages = new();
		private readonly HashSet<string> _topics;
		private int _disposed;

		public ConsumerSubscription(IEnumerable<string> topics) {
			_topics = new HashSet<string>(topics);
		}

		public void Dispose() {
			if (Interlocked.Exchange(ref _disposed, 1) == 0) {
				_messages.CompleteAdding();
				_messages.Dispose();
			}
		}

		internal bool IsSubscribedTo(string topic) {
			return _topics.Contains(topic);
		}

		internal void Add(ConsumeResult<byte[], byte[]> message) {
			if (_disposed == 0 && _topics.Contains(message.Topic))
				_messages.TryAdd(message);
		}

		internal bool TryTake(out ConsumeResult<byte[], byte[]>? message, CancellationToken cancellationToken) {
			try {
				return _messages.TryTake(out message, Timeout.Infinite, cancellationToken);
			}
			catch (OperationCanceledException) {
				message = default;
				return false;
			}
		}
	}
}

internal sealed class TestKafkaConsumer<TKey, TValue> : IConsumer<TKey, TValue> {
	private readonly TestKafkaConsumerBuilder<TKey, TValue> _builder;
	private readonly TestKafkaCluster _cluster;
	private int _disposed;
	private string? _groupId;
	private int _subscribeCalled;
	private TestKafkaCluster.ConsumerSubscription? _subscription;

	public TestKafkaConsumer(TestKafkaCluster cluster, TestKafkaConsumerBuilder<TKey, TValue> builder) {
		_cluster = cluster;
		_builder = builder;
	}

	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
			if (_subscription != null && _groupId != null) {
				_cluster.RemoveSubscription(_groupId, _subscription);
				_subscription.Dispose();
			}
	}

	public int AddBrokers(string brokers) {
		return 0;
	}

	public void SetSaslCredentials(string username, string password) {
	}

	public Handle Handle { get; } = new();
	public string Name { get; } = "TestConsumer";

	public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) {
		if (_subscription == null)
			throw new InvalidOperationException("Must call Subscribe first");

		if (_disposed != 0)
			throw new ObjectDisposedException(nameof(TestKafkaConsumer<TKey, TValue>));

		ConsumeResult<byte[], byte[]>? rawMessage;
		using (var cts = new CancellationTokenSource(millisecondsTimeout)) {
			try {
				if (!_subscription.TryTake(out rawMessage, cts.Token) || rawMessage is null)
					return null!;
			} catch (OperationCanceledException) {
				return null!;
			}
		}

		if (rawMessage.IsPartitionEOF)
			return new ConsumeResult<TKey, TValue> {
				IsPartitionEOF = true,
				Topic = rawMessage.Topic,
				Partition = rawMessage.Partition,
				Offset = rawMessage.Offset
			};

		var keyDeserializer = _builder.KafkaKeyDeserializer;
		var valueDeserializer = _builder.KafkaValueDeserializer;

		var keyContext = new SerializationContext(MessageComponentType.Key, rawMessage.Topic, rawMessage.Message.Headers);
		var valueContext = new SerializationContext(MessageComponentType.Value, rawMessage.Topic, rawMessage.Message.Headers);

		TKey key;
		if (keyDeserializer != null)
			key = keyDeserializer.Deserialize(rawMessage.Message.Key, false, keyContext);
		else if (typeof(TKey) == typeof(byte[]))
			key = (TKey)(object)rawMessage.Message.Key;
		else
			key = default!;

		TValue value;
		var valueIsNull = rawMessage.Message.Value is null;
		if (valueDeserializer != null)
			value = valueDeserializer.Deserialize(rawMessage.Message.Value, valueIsNull, valueContext);
		else if (typeof(TValue) == typeof(byte[]))
			value = (TValue)(object)rawMessage.Message.Value!;
		else
			value = default!;

		return new ConsumeResult<TKey, TValue> {
			Topic = rawMessage.Topic,
			Partition = rawMessage.Partition,
			Offset = rawMessage.Offset,
			Message = new Message<TKey, TValue> {
				Key = key,
				Value = value,
				Timestamp = rawMessage.Message.Timestamp,
				Headers = rawMessage.Message.Headers
			}
		};
	}

	public ConsumeResult<TKey, TValue> Consume(TimeSpan timeout) {
		return Consume(new CancellationTokenSource(timeout).Token);
	}

	public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken = new()) {
		if (_subscription == null)
			throw new InvalidOperationException("Must call Subscribe first");

		if (_disposed != 0)
			throw new ObjectDisposedException(nameof(TestKafkaConsumer<TKey, TValue>));

		if (!_subscription.TryTake(out var rawMessage, cancellationToken) || rawMessage is null)
			throw new OperationCanceledException();

		if (rawMessage.IsPartitionEOF)
			return new ConsumeResult<TKey, TValue> {
				IsPartitionEOF = true,
				Topic = rawMessage.Topic,
				Partition = rawMessage.Partition,
				Offset = rawMessage.Offset
			};

		// Deserialize
		var keyDeserializer = _builder.KafkaKeyDeserializer;
		var valueDeserializer = _builder.KafkaValueDeserializer;

		var keyContext = new SerializationContext(MessageComponentType.Key, rawMessage.Topic, rawMessage.Message.Headers);
		var valueContext =
			new SerializationContext(MessageComponentType.Value, rawMessage.Topic, rawMessage.Message.Headers);

		TKey key;
		if (keyDeserializer != null)
			key = keyDeserializer.Deserialize(rawMessage.Message.Key, false, keyContext);
		else if (typeof(TKey) == typeof(byte[]))
			key = (TKey)(object)rawMessage.Message.Key;
		else
			key = default!;

		TValue value;
		var valueIsNull = rawMessage.Message.Value is null;
		if (valueDeserializer != null)
			value = valueDeserializer.Deserialize(rawMessage.Message.Value, valueIsNull, valueContext);
		else if (typeof(TValue) == typeof(byte[]))
			value = (TValue)(object)rawMessage.Message.Value!;
		else
			value = default!;

		return new ConsumeResult<TKey, TValue> {
			Topic = rawMessage.Topic,
			Partition = rawMessage.Partition,
			Offset = rawMessage.Offset,
			Message = new Message<TKey, TValue> {
				Key = key,
				Value = value,
				Timestamp = rawMessage.Message.Timestamp,
				Headers = rawMessage.Message.Headers
			}
		};
	}

	public void Subscribe(IEnumerable<string> topics) {
		if (Interlocked.CompareExchange(ref _subscribeCalled, 1, 0) != 0)
			throw new InvalidOperationException("Subscribe can only be called once");

		Subscription = topics.ToList();
		// Use group.id from config, or generate unique ID if not provided (like independent consumer)
		_groupId = _builder.GetGroupId() ?? Guid.NewGuid().ToString();
		_subscription = _cluster.AddSubscription(_groupId, Subscription);

		// Invoke partition assigned handler if set (simulates Kafka rebalance callback)
		var handler = _builder.KafkaPartitionsAssignedHandler;
		if (handler is not null) {
			var partitions = Subscription.Select(t => new TopicPartition(t, 0)).ToList();
			handler(this, partitions);
		}
	}

	public void Subscribe(string topic) {
		Subscribe(new[] { topic });
	}

	public void Unsubscribe() {
		if (_subscription != null && _groupId != null) {
			_cluster.RemoveSubscription(_groupId, _subscription);
			_subscription.Dispose();
			_subscription = null;
			_subscribeCalled = 0;
		}
	}

	public void Assign(TopicPartition partition) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void Assign(TopicPartitionOffset partition) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void Assign(IEnumerable<TopicPartitionOffset> partitions) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void Assign(IEnumerable<TopicPartition> partitions) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void IncrementalAssign(IEnumerable<TopicPartition> partitions) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void Unassign() {
		throw new NotSupportedException("TestKafkaConsumer does not support manual partition assignment");
	}

	public void StoreOffset(ConsumeResult<TKey, TValue> result) {
	}

	public void StoreOffset(TopicPartitionOffset offset) {
	}

	public List<TopicPartitionOffset> Commit() {
		return new List<TopicPartitionOffset>();
	}

	public void Commit(IEnumerable<TopicPartitionOffset> offsets) {
	}

	public void Commit(ConsumeResult<TKey, TValue> result) {
	}

	public void Seek(TopicPartitionOffset tpo) {
	}

	public void Pause(IEnumerable<TopicPartition> partitions) {
	}

	public void Resume(IEnumerable<TopicPartition> partitions) {
	}

	public List<TopicPartitionOffset> Committed(TimeSpan timeout) {
		return new List<TopicPartitionOffset>();
	}

	public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) {
		return new List<TopicPartitionOffset>();
	}

	public Offset Position(TopicPartition partition) {
		return Offset.Unset;
	}

	public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch,
		TimeSpan timeout) {
		throw new NotSupportedException("TestKafkaConsumer does not support offset queries");
	}

	public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) {
		return _cluster.QueryWatermarkOffsets(topicPartition.Topic);
	}

	public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) {
		return _cluster.QueryWatermarkOffsets(topicPartition.Topic);
	}

	public void Close() {
		Dispose();
	}

	public string MemberId { get; } = Guid.NewGuid().ToString();
	public List<TopicPartition> Assignment => Subscription.Select(t => new TopicPartition(t, 0)).ToList();
	public List<string> Subscription { get; private set; } = new();

	public IConsumerGroupMetadata ConsumerGroupMetadata { get; } = new EmptyConsumerGroupMetadata();

	private sealed class EmptyConsumerGroupMetadata : IConsumerGroupMetadata {
	}
}

internal sealed class TestKafkaProducer<TKey, TValue> : IProducer<TKey, TValue> {
	private readonly TestKafkaProducerBuilder<TKey, TValue> _builder;
	private readonly TestKafkaCluster _cluster;
	private int _disposed;

	public TestKafkaProducer(TestKafkaCluster cluster, TestKafkaProducerBuilder<TKey, TValue> builder) {
		_cluster = cluster;
		_builder = builder;
	}

	public void Dispose() {
		Interlocked.Exchange(ref _disposed, 1);
	}

	public int AddBrokers(string brokers) {
		return 0;
	}

	public void SetSaslCredentials(string username, string password) {
	}

	public Handle Handle { get; } = new();
	public string Name { get; } = "TestProducer";

	public void Produce(string topic, Message<TKey, TValue> message,
		Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) {
		if (_disposed != 0)
			throw new ObjectDisposedException(nameof(TestKafkaProducer<TKey, TValue>));

		var keySerializer = _builder.KafkaKeySerializer;
		var valueSerializer = _builder.KafkaValueSerializer;

		var keyContext = new SerializationContext(MessageComponentType.Key, topic, message.Headers);
		var valueContext = new SerializationContext(MessageComponentType.Value, topic, message.Headers);

		byte[]? keyBytes;
		if (keySerializer != null)
			keyBytes = keySerializer.Serialize(message.Key, keyContext);
		else if (typeof(TKey) == typeof(byte[]))
			keyBytes = (byte[]?)(object?)message.Key;
		else
			keyBytes = null;

		byte[]? valueBytes;
		if (valueSerializer != null)
			valueBytes = valueSerializer.Serialize(message.Value, valueContext);
		else if (typeof(TValue) == typeof(byte[]))
			valueBytes = (byte[]?)(object?)message.Value;
		else
			valueBytes = null;

		var rawMessage = new ConsumeResult<byte[], byte[]> {
			Message = new Message<byte[], byte[]> {
				Key = keyBytes!,
				Value = valueBytes!,
				Headers = message.Headers ?? new Headers(),
				Timestamp = message.Timestamp
			}
		};

		try {
			_cluster.Produce(topic, rawMessage);

			deliveryHandler?.Invoke(new DeliveryReport<TKey, TValue> {
				Topic = topic,
				Partition = 0,
				Offset = rawMessage.Offset,
				Message = message,
				Status = PersistenceStatus.Persisted,
				Error = ErrorCode.NoError
			});
		}
		catch (Exception ex) {
			deliveryHandler?.Invoke(new DeliveryReport<TKey, TValue> {
				Topic = topic,
				Partition = 0,
				Offset = Offset.Unset,
				Message = message,
				Status = PersistenceStatus.NotPersisted,
				Error = new Error(ErrorCode.Local_Application, ex.Message)
			});
			throw;
		}
	}

	public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message,
		Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) {
		if (topicPartition.Partition != 0)
			throw new NotSupportedException("TestKafkaProducer only supports partition 0");

		Produce(topicPartition.Topic, message, deliveryHandler);
	}

	public Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message,
		CancellationToken cancellationToken = default) {
		if (_disposed != 0)
			return Task.FromException<DeliveryResult<TKey, TValue>>(
				new ObjectDisposedException(nameof(TestKafkaProducer<TKey, TValue>)));

		var tcs = new TaskCompletionSource<DeliveryResult<TKey, TValue>>();

		if (cancellationToken.IsCancellationRequested) {
			tcs.SetCanceled(cancellationToken);
			return tcs.Task;
		}

		try {
			Produce(topic, message, report => {
				if (report.Error.IsError)
					tcs.SetException(new ProduceException<TKey, TValue>(report.Error, report));
				else
					tcs.SetResult(new DeliveryResult<TKey, TValue> {
						Topic = report.Topic,
						Partition = report.Partition,
						Offset = report.Offset,
						Message = report.Message,
						Status = report.Status,
						TopicPartitionOffset = new TopicPartitionOffset(report.Topic, report.Partition, report.Offset)
					});
			});
		}
		catch (Exception ex) {
			tcs.SetException(ex);
		}

		return tcs.Task;
	}

	public Task<DeliveryResult<TKey, TValue>> ProduceAsync(TopicPartition topicPartition, Message<TKey, TValue> message,
		CancellationToken cancellationToken = default) {
		if (topicPartition.Partition != 0)
			return Task.FromException<DeliveryResult<TKey, TValue>>(
				new NotSupportedException("TestKafkaProducer only supports partition 0"));

		return ProduceAsync(topicPartition.Topic, message, cancellationToken);
	}

	public int Poll(TimeSpan timeout) {
		return 0;
	}

	public int Flush(TimeSpan timeout) {
		return 0;
	}

	public void Flush(CancellationToken cancellationToken = default) {
	}

	public void InitTransactions(TimeSpan timeout) {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void BeginTransaction() {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void CommitTransaction(TimeSpan timeout) {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void CommitTransaction() {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void AbortTransaction(TimeSpan timeout) {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void AbortTransaction() {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}

	public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata,
		TimeSpan timeout) {
		throw new NotSupportedException("TestKafkaProducer does not support transactions");
	}
}

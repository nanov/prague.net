namespace Prague.Kafka.TestAdaptor.Tests;

using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class TestKafkaClusterTests {
	[SetUp]
	public void Setup() {
		_services = new ServiceCollection();
		_provider = _services.AddKafkaCacheTestCluster();
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { TestTopic });
	}

	private ServiceCollection _services;
	private IKafkaCacheTestBuilderProvider _provider;
	private const string TestTopic = "test-topic";

	[Test]
	public void AddTopic_CreatesNewTopic() {
		// Arrange
		const string newTopic = "new-topic";

		// Act
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { newTopic });

		// Assert - should not throw when subscribing
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();
		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		Assert.DoesNotThrow(() => consumer.Subscribe(newTopic));
		consumer.Dispose();
	}

	[Test]
	public void Subscribe_ToNonExistentTopic_ThrowsKafkaException() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();
		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		// Act & Assert
		Assert.Throws<KafkaException>(() => consumer.Subscribe("non-existent-topic"));
		consumer.Dispose();
	}

	[Test]
	public void ProduceAndConsume_SimpleMessage_Success() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);

		// Consume EOF marker
		var eofResult = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(eofResult.IsPartitionEOF, Is.True);

		// Act
		producer.Produce(TestTopic, new Message<string, string> {
			Key = "test-key",
			Value = "test-value"
		});

		// Assert
		var result = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result.Message.Key, Is.EqualTo("test-key"));
		Assert.That(result.Message.Value, Is.EqualTo("test-value"));
		Assert.That(result.Topic, Is.EqualTo(TestTopic));
		Assert.That(result.Partition.Value, Is.EqualTo(0));
		Assert.That(result.Offset.Value, Is.EqualTo(0));

		producer.Dispose();
		consumer.Dispose();
	}

	[Test]
	public async Task ProduceAsync_SimpleMessage_Success() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);
		consumer.Consume(TimeSpan.FromSeconds(1)); // EOF

		// Act
		var deliveryResult = await producer.ProduceAsync(TestTopic, new Message<string, string> {
			Key = "async-key",
			Value = "async-value"
		});

		// Assert
		Assert.That(deliveryResult.Status, Is.EqualTo(PersistenceStatus.Persisted));
		Assert.That(deliveryResult.Topic, Is.EqualTo(TestTopic));
		Assert.That(deliveryResult.Offset.Value, Is.EqualTo(0));

		var result = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result.Message.Key, Is.EqualTo("async-key"));
		Assert.That(result.Message.Value, Is.EqualTo("async-value"));

		producer.Dispose();
		consumer.Dispose();
	}

	[Test]
	public void MultipleConsumers_ReceiveSameMessage() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer1 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		var consumer2 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer1.Subscribe(TestTopic);
		consumer2.Subscribe(TestTopic);

		consumer1.Consume(TimeSpan.FromSeconds(1)); // EOF
		consumer2.Consume(TimeSpan.FromSeconds(1)); // EOF

		// Act
		producer.Produce(TestTopic, new Message<string, string> {
			Key = "shared-key",
			Value = "shared-value"
		});

		// Assert
		var result1 = consumer1.Consume(TimeSpan.FromSeconds(1));
		var result2 = consumer2.Consume(TimeSpan.FromSeconds(1));

		Assert.That(result1.Message.Key, Is.EqualTo("shared-key"));
		Assert.That(result1.Message.Value, Is.EqualTo("shared-value"));
		Assert.That(result2.Message.Key, Is.EqualTo("shared-key"));
		Assert.That(result2.Message.Value, Is.EqualTo("shared-value"));

		producer.Dispose();
		consumer1.Dispose();
		consumer2.Dispose();
	}

	[Test]
	public void Produce_WithHeaders_PreservesHeaders() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);
		consumer.Consume(TimeSpan.FromSeconds(1)); // EOF

		var headers = new Headers {
			{ "header1", Encoding.UTF8.GetBytes("value1") },
			{ "header2", Encoding.UTF8.GetBytes("value2") }
		};

		// Act
		producer.Produce(TestTopic, new Message<string, string> {
			Key = "key",
			Value = "value",
			Headers = headers
		});

		// Assert
		var result = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result.Message.Headers, Is.Not.Null);
		Assert.That(result.Message.Headers.Count, Is.EqualTo(2));
		Assert.That(Encoding.UTF8.GetString(result.Message.Headers[0].GetValueBytes()), Is.EqualTo("value1"));
		Assert.That(Encoding.UTF8.GetString(result.Message.Headers[1].GetValueBytes()), Is.EqualTo("value2"));

		producer.Dispose();
		consumer.Dispose();
	}

	[Test]
	public void MultipleMessages_MaintainOrder() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);
		consumer.Consume(TimeSpan.FromSeconds(1)); // EOF

		// Act
		for (var i = 0; i < 10; i++)
			producer.Produce(TestTopic, new Message<string, string> {
				Key = $"key-{i}",
				Value = $"value-{i}"
			});

		// Assert
		for (var i = 0; i < 10; i++) {
			var result = consumer.Consume(TimeSpan.FromSeconds(1));
			Assert.That(result.Message.Key, Is.EqualTo($"key-{i}"));
			Assert.That(result.Message.Value, Is.EqualTo($"value-{i}"));
			Assert.That(result.Offset.Value, Is.EqualTo(i));
		}

		producer.Dispose();
		consumer.Dispose();
	}

	[Test]
	public void SubscribeAfterProduce_ReceivesExistingMessages() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		// Act - produce before subscribing
		producer.Produce(TestTopic, new Message<string, string> {
			Key = "key1",
			Value = "value1"
		});
		producer.Produce(TestTopic, new Message<string, string> {
			Key = "key2",
			Value = "value2"
		});

		// Subscribe after messages are produced
		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);

		// Assert - should receive existing messages
		var result1 = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result1.Message.Key, Is.EqualTo("key1"));

		var result2 = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result2.Message.Key, Is.EqualTo("key2"));

		var eof = consumer.Consume(TimeSpan.FromSeconds(1));
		Assert.That(eof.IsPartitionEOF, Is.True);

		producer.Dispose();
		consumer.Dispose();
	}

	[Test]
	public void Dispose_ConsumerAndProducer_NoExceptions() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);

		// Act & Assert
		Assert.DoesNotThrow(() => {
			producer.Dispose();
			consumer.Dispose();
		});
	}

	[Test]
	public void Consume_AfterDispose_ThrowsObjectDisposedException() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);
		consumer.Dispose();

		// Act & Assert
		Assert.Throws<ObjectDisposedException>(() => consumer.Consume(TimeSpan.FromSeconds(1)));
	}

	[Test]
	public void Produce_AfterDispose_ThrowsObjectDisposedException() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		producer.Dispose();

		// Act & Assert
		Assert.Throws<ObjectDisposedException>(() => producer.Produce(TestTopic, new Message<string, string> {
			Key = "key",
			Value = "value"
		}));
	}

	[Test]
	public void Unsubscribe_AllowsResubscribe() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var consumer = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer.Subscribe(TestTopic);

		// Act
		consumer.Unsubscribe();

		// Assert
		Assert.DoesNotThrow(() => consumer.Subscribe(TestTopic));

		consumer.Dispose();
	}

	[Test]
	public void ConsumerGroup_SameGroup_SharesMessages() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		// Two consumers in the same group
		var consumer1 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "test-group" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		var consumer2 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "test-group" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer1.Subscribe(TestTopic);
		consumer2.Subscribe(TestTopic);

		consumer1.Consume(TimeSpan.FromSeconds(1)); // EOF
		consumer2.Consume(TimeSpan.FromSeconds(1)); // EOF

		// Act - produce 10 messages
		for (var i = 0; i < 10; i++)
			producer.Produce(TestTopic, new Message<string, string> {
				Key = $"key-{i}",
				Value = $"value-{i}"
			});

		// Assert - each consumer should receive approximately half the messages (round-robin)
		var consumer1Keys = new List<string>();
		var consumer2Keys = new List<string>();

		for (var i = 0; i < 5; i++) {
			var result1 = consumer1.Consume(TimeSpan.FromSeconds(1));
			consumer1Keys.Add(result1.Message.Key);

			var result2 = consumer2.Consume(TimeSpan.FromSeconds(1));
			consumer2Keys.Add(result2.Message.Key);
		}

		// Verify round-robin distribution
		Assert.That(consumer1Keys, Is.EqualTo(new[] { "key-0", "key-2", "key-4", "key-6", "key-8" }));
		Assert.That(consumer2Keys, Is.EqualTo(new[] { "key-1", "key-3", "key-5", "key-7", "key-9" }));

		producer.Dispose();
		consumer1.Dispose();
		consumer2.Dispose();
	}

	[Test]
	public void ConsumerGroup_DifferentGroups_AllReceiveMessages() {
		// Arrange
		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		// Two consumers in different groups
		var consumer1 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "group-1" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		var consumer2 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "group-2" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer1.Subscribe(TestTopic);
		consumer2.Subscribe(TestTopic);

		consumer1.Consume(TimeSpan.FromSeconds(1)); // EOF
		consumer2.Consume(TimeSpan.FromSeconds(1)); // EOF

		// Act - produce 3 messages
		for (var i = 0; i < 3; i++)
			producer.Produce(TestTopic, new Message<string, string> {
				Key = $"key-{i}",
				Value = $"value-{i}"
			});

		// Assert - both consumers should receive all messages
		var consumer1Keys = new List<string>();
		var consumer2Keys = new List<string>();

		for (var i = 0; i < 3; i++) {
			var result1 = consumer1.Consume(TimeSpan.FromSeconds(1));
			consumer1Keys.Add(result1.Message.Key);

			var result2 = consumer2.Consume(TimeSpan.FromSeconds(1));
			consumer2Keys.Add(result2.Message.Key);
		}

		Assert.That(consumer1Keys, Is.EqualTo(new[] { "key-0", "key-1", "key-2" }));
		Assert.That(consumer2Keys, Is.EqualTo(new[] { "key-0", "key-1", "key-2" }));

		producer.Dispose();
		consumer1.Dispose();
		consumer2.Dispose();
	}

	[Test]
	public void ConsumerGroup_DifferentTopicSubscriptions_OnlyReceiveSubscribedTopics() {
		// Arrange
		const string topic1 = "topic-1";
		const string topic2 = "topic-2";
		KafkaCacheTestBuilderProviderMarshall.AddTopics(_provider, new[] { topic1, topic2 });

		var sp = _services.BuildServiceProvider();
		var builderProvider = sp.GetRequiredService<IKafkaCacheBuilderProvider>();

		var producer = builderProvider.NewProducerBuilder<string, string>(new Dictionary<string, string>())
			.SetKeySerializer(Serializers.Utf8)
			.SetValueSerializer(Serializers.Utf8)
			.Build();

		// Two consumers in the same group but subscribed to different topics
		var consumer1 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "mixed-group" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		var consumer2 = builderProvider.NewConsumerBuilder<string, string>(new Dictionary<string, string> {
				{ "group.id", "mixed-group" }
			})
			.SetKeyDeserializer(Deserializers.Utf8)
			.SetValueDeserializer(Deserializers.Utf8)
			.Build();

		consumer1.Subscribe(topic1);
		consumer2.Subscribe(topic2);

		consumer1.Consume(TimeSpan.FromSeconds(1)); // EOF topic1
		consumer2.Consume(TimeSpan.FromSeconds(1)); // EOF topic2

		// Act - produce to both topics
		producer.Produce(topic1, new Message<string, string> { Key = "topic1-msg1", Value = "val1" });
		producer.Produce(topic2, new Message<string, string> { Key = "topic2-msg1", Value = "val2" });
		producer.Produce(topic1, new Message<string, string> { Key = "topic1-msg2", Value = "val3" });
		producer.Produce(topic2, new Message<string, string> { Key = "topic2-msg2", Value = "val4" });

		// Assert - each consumer only receives messages from their subscribed topic
		var result1a = consumer1.Consume(TimeSpan.FromSeconds(1));
		var result1b = consumer1.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result1a.Message.Key, Is.EqualTo("topic1-msg1"));
		Assert.That(result1b.Message.Key, Is.EqualTo("topic1-msg2"));

		var result2a = consumer2.Consume(TimeSpan.FromSeconds(1));
		var result2b = consumer2.Consume(TimeSpan.FromSeconds(1));
		Assert.That(result2a.Message.Key, Is.EqualTo("topic2-msg1"));
		Assert.That(result2b.Message.Key, Is.EqualTo("topic2-msg2"));

		producer.Dispose();
		consumer1.Dispose();
		consumer2.Dispose();
	}
}
namespace Prague.Kafka.IntegrationTests;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

[SetUpFixture]
public class DualKafkaClusterFixture : IDisposable {
    private KafkaContainer _clusterA = null!;
    private KafkaContainer _clusterB = null!;

    public static DualKafkaClusterFixture Instance { get; private set; } = null!;

    public static string BootstrapServersA => Instance._clusterA.GetBootstrapAddress();
    public static string BootstrapServersB => Instance._clusterB.GetBootstrapAddress();

    [OneTimeSetUp]
    public async Task GlobalSetup() {
        Instance = this;

        _clusterA = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.0")
            .Build();

        _clusterB = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.0")
            .Build();

        await Task.WhenAll(_clusterA.StartAsync(), _clusterB.StartAsync());
    }

    [OneTimeTearDown]
    public void GlobalTeardown() {
        Dispose();
    }

    public void Dispose() {
        _clusterA?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _clusterB?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public static async Task CreateTopicAsync(string bootstrapServers, string name, int partitions = 1) {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        try {
            await admin.CreateTopicsAsync([
                new TopicSpecification { Name = name, NumPartitions = partitions, ReplicationFactor = 1 }
            ]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists)) {
        }
    }

    public static IProducer<byte[], byte[]> NewProducer(string bootstrapServers) {
        return new ProducerBuilder<byte[], byte[]>(new ProducerConfig {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All
        }).Build();
    }

    public static IConsumer<byte[], byte[]> NewConsumer(string bootstrapServers, string? groupId = null) {
        return new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig {
            BootstrapServers = bootstrapServers,
            GroupId = groupId ?? Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = true
        }).Build();
    }
}

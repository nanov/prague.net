namespace Prague.Kafka.IntegrationTests;

using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[TestFixture]
public class DualClusterCacheTests {
    private const string TopicProducts = "integration-tests-products";
    private const string TopicOrders = "integration-tests-orders";

    [SetUp]
    public async Task Setup() {
        _topicSuffix = Guid.NewGuid().ToString("N")[..8];
        _productTopic = $"{TopicProducts}-{_topicSuffix}";
        _orderTopic = $"{TopicOrders}-{_topicSuffix}";

        await Task.WhenAll(
            DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersA, _productTopic),
            DualKafkaClusterFixture.CreateTopicAsync(DualKafkaClusterFixture.BootstrapServersB, _orderTopic));
    }

    private string _topicSuffix = "";
    private string _productTopic = "";
    private string _orderTopic = "";

    private (IServiceProvider sp, IHostedService hosted) BuildServiceProvider(
        string bootstrapServers, string configSection, Action<KafkaCacheHandlersBuilder> configure) {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                { $"{configSection}:BootstrapServers", bootstrapServers }
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddKafkaCaches(configSection, configure);

        var sp = services.BuildServiceProvider();
        var hosted = sp.GetRequiredService<IHostedService>();
        return (sp, hosted);
    }

    [Test]
    public async Task SingleApp_TwoClusters_BothCachesLoadSimultaneously() {
        using var producerA = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersA);
        using var producerB = DualKafkaClusterFixture.NewProducer(DualKafkaClusterFixture.BootstrapServersB);

        for (var i = 1; i <= 3; i++) {
            var product = new Product { Id = i, Name = $"Product-{i}", Price = i * 10m, Category = "Cat" };
            producerA.Produce(_productTopic, new Confluent.Kafka.Message<byte[], byte[]> {
                Key = MessagePack.MessagePackSerializer.Serialize(i), Value = MessagePack.MessagePackSerializer.Serialize(product)
            });
        }

        for (var i = 1; i <= 2; i++) {
            var order = new Order { Id = i, ProductId = i, Quantity = i * 2, Status = "pending" };
            producerB.Produce(_orderTopic, new Confluent.Kafka.Message<byte[], byte[]> {
                Key = MessagePack.MessagePackSerializer.Serialize(i), Value = MessagePack.MessagePackSerializer.Serialize(order)
            });
        }

        producerA.Flush(TimeSpan.FromSeconds(5));
        producerB.Flush(TimeSpan.FromSeconds(5));

        // Single service collection - two AddKafkaCaches calls, each pointing to a different cluster
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                { "ClusterA:BootstrapServers", DualKafkaClusterFixture.BootstrapServersA },
                { "ClusterB:BootstrapServers", DualKafkaClusterFixture.BootstrapServersB }
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddKafkaCaches("ClusterA", (g) => {
		        g.BootstrapServers = DualKafkaClusterFixture.BootstrapServersA;
		        g.Vars["topicSuffix"] = _topicSuffix;
	        },
	        b => {
		        b.AddCache<ProductCache, int, Product>(_productTopic);
	        });
        services.AddKafkaCaches("ClusterB", (g) => {
	        g.BootstrapServers = DualKafkaClusterFixture.BootstrapServersB;
	        g.Vars["topicSuffix"] = _topicSuffix;
        },b => {
            b.AddCache<OrderCache, int, Order>(_orderTopic);
        });

        var sp = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var hostedServices = sp.GetServices<IHostedService>().ToList();
        await Task.WhenAll(hostedServices.Select(h => h.StartAsync(cts.Token)));

        var loader = sp.GetRequiredService<KafkaCachesLoader>();
        await loader.StartAsync(cts.Token);

        var productCache = sp.GetRequiredService<ProductCache>();
        var orderCache = sp.GetRequiredService<OrderCache>();

        Assert.That(productCache.Cache.Count, Is.EqualTo(3), "Products from cluster A");
        Assert.That(orderCache.Cache.Count, Is.EqualTo(2), "Orders from cluster B");

        Assert.That(productCache.Cache.TryGet(2, out var p2), Is.True);
        Assert.That(p2!.Name, Is.EqualTo("Product-2"));

        Assert.That(orderCache.Cache.TryGet(1, out var o1), Is.True);
        Assert.That(o1!.ProductId, Is.EqualTo(1));

        await Task.WhenAll(hostedServices.Select(h => h.StopAsync(CancellationToken.None)));
    }

}

// docs: every code block of www/docs/articles/how-to/host-with-di.md — registering caches in a generic host
// and an ASP.NET Core host, binding KafkaCachesOptions from IConfiguration, topic placeholders, several
// clusters, the registry, and shutdown. The Program-shaped blocks are compiled-only; options binding and
// resolution run as tests because building the container contacts no broker.
namespace Prague.Samples.HowTo;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Prague.Api;
using Prague.Core;
using Prague.Kafka;
using Prague.Kafka.IO;
using Prague.Kafka.Options;
using Prague.Samples.Models;

public static class HostWithDiPrograms {
	#region host-generic
	public static IHost BuildWorker(string[] args) {
		var builder = Host.CreateApplicationBuilder(args);

		// Options come from the "kafkaCaches" section of IConfiguration (appsettings, env vars, ...).
		builder.Services.AddKafkaCaches(b => {
			b.AddCache<OrderCache, string, Order>();
			b.AddCache<CustomerCache, int, Customer>();
		});
		builder.Services.AddHostedService<OrderReportWorker>();  // starts after the caches are loaded: see below

		return builder.Build();                                  // host.Run(): StartAsync blocks until every topic hit EOF
	}
	#endregion

	#region host-aspnet
	public static WebApplication BuildApi(string[] args) {
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddKafkaCaches("kafkaCaches",          // explicit section name; same default as above
			(o, sp) => o.ClientSettings["client.id"] = sp.GetRequiredService<IHostEnvironment>().ApplicationName,
			b => b.AddCache<OrderCache, string, Order>());

		var app = builder.Build();
		app.MapPragueEndpoints();                                // GET /prague, /prague/OrderCache?customerId=42
		app.MapGet("/orders/{id}", (string id, OrderCache orders)
			=> orders.Cache.TryGet(id, out var order) ? Results.Ok(order) : Results.NotFound());
		return app;                                              // app.Run() serves only after the initial load
	}
	#endregion

	#region host-two-clusters
	public static void TwoClusters(IServiceCollection services) {
		services.AddKafkaCaches("ordersCluster", b => b.AddCache<OrderCache, string, Order>());
		services.AddKafkaCaches("catalogCluster", b => {
			b.AddCache<ProductCache, int, Product>();
			b.AddInternalCache<ShipmentCache, string, Shipment>();   // in the registry and joinable, never consumed
		});
	}

	// One hosted worker drives every cluster; each section owns a producer, a KEYED singleton under the section name.
	public sealed class OrderPublisher([FromKeyedServices("ordersCluster")] KafkaCacheProducer producer, OrderCache orders) {
		public void Place(Order order) => orders.AddOrUpdateAndProduce(order);   // local write + produce (only if changed)

		public void Cancel(string orderId) => orders.RemoveAndProduce(orderId);   // local remove + tombstone

		public void Mirror(Order order) => producer.Produce("orders.archive", order.OrderId, order);   // any topic
	}
	#endregion

	#region host-after-load-worker
	// Wait for the load inside ExecuteAsync, never in StartAsync: the host starts hosted services one after another,
	// so StartAsync returning at the first await is what lets the Kafka worker start, whatever the registration order.
	public sealed class OrderReportWorker(IDataCacheRegistry registry, OrderCache orders) : BackgroundService {
		protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
			await registry.LoadingCompletion.WaitAsync(stoppingToken);
			while (!stoppingToken.IsCancellationRequested) {
				Console.WriteLine($"{orders.Query().WithStatus(OrderStatus.Pending).Count()} pending orders");
				await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
			}
		}
	}
	#endregion

	#region host-shutdown
	public static async Task StopGracefully(IHost host) {
		// StopAsync cancels the consume loops and waits for each cache's live worker to drain its queue,
		// bounded by the token you pass (the host passes HostOptions.ShutdownTimeout, 30 s by default).
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		await host.StopAsync(cts.Token);                         // anything still draining is logged by name, not thrown
	}
	#endregion
}

[TestFixture]
public class HostWithDi {
	private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
		new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

	[Test]
	public void OptionsBindFromTheConfigurationSection() {
		var services = new ServiceCollection();
		services.AddLogging();

		#region host-options-binding
		// appsettings.json:  { "kafkaCaches": { "BootstrapServers": "kafka:9092",
		//                      "ClientSettings": { "group.id": "order-api" }, "Vars": { "env": "staging" } } }
		services.AddSingleton(Config(
			("kafkaCaches:BootstrapServers", "kafka:9092"),
			("kafkaCaches:ClientSettings:group.id", "order-api"),
			("kafkaCaches:Vars:env", "staging")));

		services.AddKafkaCaches(b => b.AddCache<OrderCache, string, Order>());   // binds "kafkaCaches" first ...

		using var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get("kafkaCaches");
		var orders = provider.GetRequiredService<OrderCache>();

		// ... and the [v:env] placeholder in [DataCacheTopic("orders.[v:env]")] resolved from Vars.
		Console.WriteLine(orders.Topic);                         // orders.staging
		#endregion

		Assert.That(options.BootstrapServers, Is.EqualTo("kafka:9092"));
		Assert.That(options.ClientSettings["group.id"], Is.EqualTo("order-api"));
		Assert.That(orders.Topic, Is.EqualTo("orders.staging"));
	}

	[Test]
	public void CodeOverridesConfiguration() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Config(("kafkaCaches:BootstrapServers", "from-config"), ("kafkaCaches:Vars:env", "staging")));

		#region host-options-code-override
		services.AddKafkaCaches(
			o => {
				o.BootstrapServers = "from-code";                   // runs after the IConfiguration bind: code wins
				o.Vars["env"] = "prod";
			},
			b => b.AddCache<OrderCache, string, Order>("orders.[v:env].[e:HOSTNAME]"));   // [e:NAME] = environment variable
		#endregion

		Environment.SetEnvironmentVariable("HOSTNAME", "node-1");
		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get("kafkaCaches").BootstrapServers, Is.EqualTo("from-code"));
		Assert.That(provider.GetRequiredService<OrderCache>().Topic, Is.EqualTo("orders.prod.node-1"));
	}

	[Test]
	public void AnUnresolvedPlaceholderIsLeftAsIs() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Config(("kafkaCaches:BootstrapServers", "kafka:9092")));
		services.AddKafkaCaches(b => b.AddCache<OrderCache, string, Order>());
		using var provider = services.BuildServiceProvider();

		// No Vars["env"]: the template is used verbatim, so the consumer would subscribe to a topic literally named
		// "orders.[v:env]". Check Topic at startup if your templates carry placeholders.
		Assert.That(provider.GetRequiredService<OrderCache>().Topic, Is.EqualTo("orders.[v:env]"));
	}

	[Test]
	public void TheRegistryListsEveryCache() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Config(("kafkaCaches:BootstrapServers", "kafka:9092")));
		services.AddKafkaCaches(b => b.AddCache<OrderCache, string, Order>());
		using var provider = services.BuildServiceProvider();

		#region host-registry
		var registry = provider.GetRequiredService<IDataCacheRegistry>();
		var orders = registry.GetCache<OrderCache>();           // same instance as GetRequiredService<OrderCache>()
		var customers = registry.GetCache<CustomerCache>();     // pulled in by the FK: in the registry, NOT a DI service
		var activity = registry.GetGlobalIndex<CustomerActivityIndex>();   // likewise

		foreach (var info in registry.GetCaches())
			Console.WriteLine($"{info.Name}: {info.QueryableFields.Count} queryable fields");
		#endregion

		Assert.That(orders, Is.SameAs(provider.GetRequiredService<OrderCache>()));
		Assert.That(provider.GetService<CustomerCache>(), Is.Null, "only caches you AddCache<> become DI services");
		Assert.That(customers, Is.Not.Null);
		Assert.That(activity, Is.Not.Null);
		// Registering OrderCache pulls in every cache its foreign keys reach, transitively.
		Assert.That(registry.GetCaches().Select(c => c.Name),
			Is.EquivalentTo(new[] { "OrderCache", "CustomerCache", "OrderLineCache", "ShipmentCache", "ProductCache" }));
	}

	[Test]
	public void TwoClustersRegisterTwoKeyedProducers() {
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Config(
			("ordersCluster:BootstrapServers", "orders-kafka:9092"),
			("catalogCluster:BootstrapServers", "catalog-kafka:9092")));
		HostWithDiPrograms.TwoClusters(services);
		using var provider = services.BuildServiceProvider();

		var monitor = provider.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>();
		Assert.That(monitor.Get("ordersCluster").BootstrapServers, Is.EqualTo("orders-kafka:9092"));
		Assert.That(monitor.Get("catalogCluster").BootstrapServers, Is.EqualTo("catalog-kafka:9092"));
		Assert.That(provider.GetServices<IHostedService>().Count(), Is.EqualTo(1), "one worker hosts every cluster");
	}
}

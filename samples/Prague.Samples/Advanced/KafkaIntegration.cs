// docs: every code block of www/docs/articles/advanced/kafka-integration.md. No broker runs here: the
// [Test]s register caches, filters, handlers and health checks against a ServiceCollection and assert
// the resulting container; the methods without [Test] are wiring that only makes sense against a live
// cluster (awaiting the initial load, producing) and are compiled, not executed.
namespace Prague.Samples.Advanced;

using System.Collections.Frozen;
using MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using OpenTelemetry.Metrics;
using Prague.Core;
using Prague.Kafka;
using Prague.Kafka.Health;
using Prague.Kafka.IO;
using Prague.Kafka.OpenTelemetry;
using Prague.Kafka.Options;
using Prague.Samples.Concepts;
using Prague.Samples.Models;

#region filter-state-services
public interface IAllowList {
	IReadOnlyCollection<string> OrderIds { get; }
}

public interface IClock {
	long CutoffUnixMs { get; }
}

public interface IRegionPolicy {
	bool Allows(int customerId);
}
#endregion

#region producer-service
public sealed class OrderService([FromKeyedServices("kafkaCaches")] KafkaCacheProducer producer) {
	public void Place(Order order) => producer.Produce("orders.prod", order.OrderId, order);   // upsert

	public void Forget(string orderId) => producer.Delete("orders.prod", orderId);             // tombstone
}
#endregion

[TestFixture]
public class KafkaIntegration {
	[Test]
	public void WiringUp() {
		var services = NewServices();

		#region wiring-up
		services.AddKafkaCaches(
			o => {
				o.BootstrapServers = "kafka:9092";
				o.ClientSettings["group.id"] = "orders-view";       // forwarded to librdkafka
				o.Vars["env"] = "prod";                             // [v:env] in a topic template → "prod"
			},
			builder => {
				builder.AddCache<OrderCache, string, Order>();                          // topic from [DataCacheTopic]: orders.[v:env]
				builder.AddCache<CustomerCache, int, Customer>("customers.[v:env]");   // explicit template
			});
		#endregion

		using var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptionsMonitor<KafkaCachesOptions>>().Get("kafkaCaches");

		Assert.That(options.BootstrapServers, Is.EqualTo("kafka:9092"));
		Assert.That(options.Vars["env"], Is.EqualTo("prod"));
		Assert.That(provider.GetRequiredService<OrderCache>(), Is.Not.Null);
		Assert.That(provider.GetRequiredService<CustomerCache>(), Is.Not.Null);
	}

	[Test]
	public void TopicTemplates() {
		#region topic-template
		var template = OrderCache.TopicNameTemplate;   // "orders.[v:env]" — [v:name] reads KafkaCachesOptions.Vars, [e:NAME] an environment variable
		#endregion

		Assert.That(template, Is.EqualTo("orders.[v:env]"));
		Assert.That(OrderLineCache.TopicNameTemplate, Is.EqualTo("Cache.OrderLine"), "without [DataCacheTopic] the template is Cache.<ClassName>");
	}

	[Test]
	public void GlobalOptions() {
		var services = NewServices();

		#region global-options
		services.Configure<KafkaCachesGlobalOptions>(o => {
			o.StatisticsEnabled = true;           // default; the broker-state readiness predicate needs it
			o.StatisticsIntervalSeconds = 60;
		});
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<IOptions<KafkaCachesGlobalOptions>>().Value.StatisticsIntervalSeconds, Is.EqualTo(60u));
	}

	[Test]
	public void CacheRegistrationForms() {
		var services = NewServices();
		services.AddSingleton(new TopicNames { Shipments = "shipments.blue" });

		#region cache-registration
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => {
			builder.AddCache<OrderCache, string, Order>();                                     // template from [DataCacheTopic]
			builder.AddCache<CustomerCache, int, Customer>("customers-v2");                    // explicit topic name
			builder.AddCache<ShipmentCache, string, Shipment>(sp =>                            // resolved from the container
				sp.GetRequiredService<TopicNames>().Shipments);
			builder.AddInternalCache<ProductCache, int, Product>();                           // in the registry, not consumed
		});
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<ProductCache>(), Is.Not.Null);
		Assert.That(provider.GetRequiredService<ShipmentCache>(), Is.Not.Null);
	}

	[Test]
	public void Filters() {
		var services = NewServices();
		var cutoffUnixMs = 1_760_000_000_000L;

		#region filters
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder
			.AddCache<OrderCache, string, Order>()
			.WithHeaderEqualsFilter("region", "EU")
			.WithHeaderEqualsFilter("region", "EU", "UK")                 // several values: OR
			.WithHeaderEqualsFilter("priority", 1, 2, 3)                  // int / long overloads
			.WithHeaderNotEqualsFilter("source", "synthetic")
			.WithHeaderExistsFilter("tenant-id")
			.WithHeaderFilter<long>("dispatched-at",
				ts => ts > cutoffUnixMs,
				passOnNull: false)                                        // what a missing header does
			.WithKeyFilter(static k => !k.StartsWith("tmp-"))
			.WithValueFilter(static o => o.Status != OrderStatus.Cancelled));
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<OrderCache>(), Is.Not.Null);
	}

	[Test]
	public void FiltersWithDiState() {
		var services = NewServices();
		services.AddSingleton<IAllowList>(new StaticAllowList(["ORD-001", "ORD-002"]));
		services.AddSingleton<IClock>(new FixedClock(1_760_000_000_000L));
		services.AddSingleton<IRegionPolicy>(new EuOnly());

		#region filters-di-state
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder
			.AddCache<OrderCache, string, Order>()
			// Snapshot — the factory runs once, at build; the predicate reads the frozen set per message.
			.WithKeyFilter(
				static sp => sp.GetRequiredService<IAllowList>().OrderIds.ToFrozenSet(),
				static (allow, key) => allow.Contains(key))
			// Several services at once: a named tuple's element names survive into the predicate.
			.WithKeyFilter(
				static sp => (allow: sp.GetRequiredService<IAllowList>(), clock: sp.GetRequiredService<IClock>()),
				static (s, key) => s.allow.OrderIds.Contains(key) && s.clock.CutoffUnixMs > 0)
			// Sugar for a single service — the type argument is never inferred, spell it.
			.WithValueFilter<IRegionPolicy>(static (policy, o) => policy.Allows(o.CustomerId))
			// Header state filter: both type arguments are explicit.
			.WithHeaderFilter<IClock, long>("dispatched-at",
				static sp => sp.GetRequiredService<IClock>(),
				static (clock, ts) => ts >= clock.CutoffUnixMs));
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<OrderCache>(), Is.Not.Null);
	}

	[Test]
	public void TreatAsDelete() {
		var services = NewServices();

		#region treat-as-delete
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder
			.AddCache<OrderCache, string, Order>()
			// A cancelled order is removed from the cache instead of being skipped.
			.WithValueFilter(static o => o.Status != OrderStatus.Cancelled, treatAsDelete: true));
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<OrderCache>(), Is.Not.Null);
	}

	[Test]
	public void AfterHandlers() {
		var services = NewServices();

		#region after-handler-registration
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder
			.AddCache<OrderCache, string, Order>()
			.WithAfterHandler<OrderProjector>());                         // ICacheAfterHandler<string, Order>, live phase only
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetServices<ICacheAfterHandler<string, Order>>().Single(), Is.InstanceOf<OrderProjector>());
	}

	[Test]
	public void HealthChecks() {
		var services = NewServices();
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder.AddCache<OrderCache, string, Order>());

		#region health-checks
		services.AddHealthChecks()
			.AddPragueKafkaLiveness(tags: ["live"])      // "prague-kafka-live", Unhealthy on failure
			.AddPragueKafkaReadiness(tags: ["ready"]);   // "prague-kafka-ready", Degraded on failure

		services.Configure<KafkaCachesHealthOptions>(o => {
			o.PollLoopHeartbeatTimeout = TimeSpan.FromSeconds(3);   // liveness
			o.HandlerProcessingTimeout = TimeSpan.FromSeconds(5);   // liveness
			o.MinBrokersUp = 1;                                     // readiness
		});
		#endregion

		using var provider = services.BuildServiceProvider();
		var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

		Assert.That(registrations.Select(r => r.Name), Is.EquivalentTo(new[] { "prague-kafka-live", "prague-kafka-ready" }));
		Assert.That(registrations.Single(r => r.Name == "prague-kafka-ready").FailureStatus, Is.EqualTo(HealthStatus.Degraded));
		Assert.That(provider.GetRequiredService<IOptions<KafkaCachesHealthOptions>>().Value.MinBrokersUp, Is.EqualTo(1));
	}

	[Test]
	public void OpenTelemetryMetrics() {
		var services = NewServices();
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", builder => builder.AddCache<OrderCache, string, Order>());

		#region otel-metrics
		services.AddOpenTelemetry().WithMetrics(m => m
			.AddPragueKafkaInstrumentation());           // "prague.kafka.*"; AddPragueKafkaInstrumentation("acme.") prefixes them
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<MeterProvider>(), Is.Not.Null);
	}

	[Test]
	public void MessagePackFormatters() {
		var services = NewServices();

		#region messagepack-resolver
		services.AddKafkaCaches(
			o => o.BootstrapServers = "kafka:9092",
			builder => builder.AddCache<OrderCache, string, Order>(),
			options => options.WithMessagePackFormatterFactory(defaultFactory =>
				MessagePackFormatterFactory.Combine(NativeDateTimeFormatterFactory.Instance, defaultFactory)));   // your formatters first, Prague's defaults after
		#endregion

		using var provider = services.BuildServiceProvider();

		Assert.That(provider.GetRequiredService<OrderCache>(), Is.Not.Null);
	}

	// Wiring that needs a live cluster: compiled, never run here.

	#region await-initial-load
	public static async Task RunHostAsync(string[] args) {
		var builder = Host.CreateApplicationBuilder(args);
		builder.Services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092",
			b => b.AddCache<OrderCache, string, Order>());

		var host = builder.Build();
		await host.DataCachesLoadCompletion();      // returns once every cache reaches its initial-load EOF
		await host.RunAsync();
	}
	#endregion

	#region health-endpoints
	public static void MapHealth(WebApplication app) {
		app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
		app.MapHealthChecks("/health/ready", new HealthCheckOptions {
			Predicate = r => r.Tags.Contains("ready"),
			ResultStatusCodes = {
				[HealthStatus.Healthy] = StatusCodes.Status200OK,
				[HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,   // the default maps Degraded to 200
				[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
			},
		});
	}
	#endregion

	#region produce-through-the-cache
	public static void WriteThrough(OrderCache orders, Order order) {
		orders.AddOrUpdateAndProduce(order);        // local write, then a Produce on the cache's own topic — only if the value changed
		orders.RemoveAndProduce(order.OrderId);     // local remove, then the tombstone; true if the key was resident here
	}
	#endregion

	#region produce-forced
	public static void RePublish(OrderCache orders, Order order) {
		orders.AddOrUpdateAndProduce(order, force: true);   // same conditional local write, but the Produce always happens
	}
	#endregion

	#region produce-all
	// Repair only: snapshots the whole store under its locks, then produces one record per resident key.
	public static int ReSeedTopic(OrderCache orders) => orders.ForceProduceAll();
	#endregion

	private static ServiceCollection NewServices() {
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());   // a host registers this for you
		return services;
	}

	private sealed class TopicNames {
		public string Shipments { get; init; } = "shipments";
	}

	private sealed class StaticAllowList(string[] orderIds) : IAllowList {
		public IReadOnlyCollection<string> OrderIds => orderIds;
	}

	private sealed class FixedClock(long cutoffUnixMs) : IClock {
		public long CutoffUnixMs => cutoffUnixMs;
	}

	private sealed class EuOnly : IRegionPolicy {
		public bool Allows(int customerId) => customerId < 1000;
	}
}

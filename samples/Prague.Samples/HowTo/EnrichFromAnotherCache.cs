// docs: every code block of www/docs/articles/how-to/enrich-from-another-cache.md — an ICacheEnricher that
// fills OrderSearchRow from CustomerCache, its registration, and what the cache holds afterwards.
namespace Prague.Samples.HowTo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Prague.Core;
using Prague.Kafka;
using Prague.Samples.Models;

#region enrich-enricher
public sealed class CustomerIntoSearchRow(CustomerCache customers) : ICacheEnricher<string, OrderSearchRow> {
	public void Enrich(string key, OrderSearchRow row) {
		if (!customers.Cache.TryGet(row.CustomerId, out var customer))
			return;                                                // unknown customer: keep the producer's row as is

		row.CustomerName = customer.Name;                          // the row is not in the cache yet: change it in place
		row.CustomerTier = customer.Tier;
	}
}
#endregion

public static class EnrichFromAnotherCacheWiring {
	#region enrich-register
	public static void Register(KafkaCacheHandlersBuilder b) {
		b.AddCache<CustomerCache, int, Customer>();                // the cache the enricher reads
		b.AddCache<OrderSearchRowCache, string, OrderSearchRow>()
			.WithEnricher<CustomerIntoSearchRow>();                 // a singleton bound to this cache, built by DI
	}
	#endregion
}

[TestFixture]
public class EnrichFromAnotherCache {
	[Test]
	public void TheIndexReflectsWhatTheEnricherWrote() {
		var customers = new CustomerCache();
		customers.AddOrUpdate(new Customer { Id = 42, Email = "ada@example.com", Country = "GB", Tier = CustomerTier.Gold, Name = "Ada" });
		var rows = new OrderSearchRowCache();
		var enricher = new CustomerIntoSearchRow(customers);

		#region enrich-by-hand
		var row = new OrderSearchRow { OrderId = "ORD-001", CustomerId = 42, Total = 100m };   // what the producer sent
		enricher.Enrich(row.OrderId, row);                                                     // what the consumer does before storing
		rows.AddOrUpdate(row);                                                                 // stored and indexed as enriched

		using var gold = rows.Query().WithCustomerTier(CustomerTier.Gold).ExecutePooled();
		#endregion

		Assert.That(row.CustomerName, Is.EqualTo("Ada"));
		Assert.That(gold.Count, Is.EqualTo(1));
	}

	[Test]
	public void AnUnknownCustomerLeavesTheRowAsProduced() {
		var enricher = new CustomerIntoSearchRow(new CustomerCache());
		var row = new OrderSearchRow { OrderId = "ORD-002", CustomerId = 7, Total = 5m };

		enricher.Enrich(row.OrderId, row);

		Assert.That(row.CustomerName, Is.Null);
		Assert.That(row.CustomerTier, Is.EqualTo(default(CustomerTier)));
	}

	[Test]
	public void RegistrationBindsTheEnricherToTheCache() {
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		services.AddLogging();
		services.AddKafkaCaches(o => o.BootstrapServers = "kafka:9092", EnrichFromAnotherCacheWiring.Register);

		using var provider = services.BuildServiceProvider();

		// Keyed by the cache type: the consumer of OrderSearchRowCache resolves it, nothing else can.
		Assert.That(provider.GetRequiredKeyedService<ICacheEnricher<string, OrderSearchRow>>(typeof(OrderSearchRowCache)),
			Is.TypeOf<CustomerIntoSearchRow>());
		Assert.That(provider.GetServices<ICacheEnricher<string, OrderSearchRow>>(), Is.Empty);
	}
}

namespace Prague.Generated.Tests.Join;

using System;
using System.Linq;
using Prague.Core;
using NUnit.Framework;

// User's reported case: CatalogProduct has a COMPOSITE tuple PK (long EventId, SalesChannel ChannelId);
// PredefinedOfferBuilder has a simple FK long EventId with a selector that fixes the Web channel.
// Requirements: (1) WithEventId(long) usable as a standalone filter (no join), (2) JoinWithCatalogProduct still works.

public enum SalesChannel { Web, Store }

public readonly struct WebCatalogProductSelector
	: IForeignKeySelector<long, (long EventId, SalesChannel ChannelId)> {
	public static (long EventId, SalesChannel ChannelId) Select(long fk) => (fk, SalesChannel.Web);
}

[DataCache]
public partial class CatalogProductRepro {
	[DataCacheKey]
	public (long EventId, SalesChannel ChannelId) Key => (EventId, ChannelId);
	public long EventId { get; set; }
	public SalesChannel ChannelId { get; set; }
	public string Name { get; set; } = string.Empty;
}

[DataCache]
public partial class PredefinedOfferBuilderRepro {
	[DataCacheKey] public Guid Id { get; set; }

	[DataCacheForeignKey<CatalogProductRepro, WebCatalogProductSelector>(DataCacheJoinType.ManyToOne)]
	public long EventId { get; set; }

	public string Label { get; set; } = string.Empty;
}

[TestFixture]
public class SelectorFkCompositeTargetReproTests {
	private DataCacheRegistry _registry = null!;
	private CatalogProductReproCache _products = null!;
	private PredefinedOfferBuilderReproCache _orders = null!;
	private readonly Guid _b1 = Guid.NewGuid();
	private readonly Guid _b2 = Guid.NewGuid();
	private readonly Guid _b3 = Guid.NewGuid();

	[SetUp]
	public void SetUp() {
		_registry = new DataCacheRegistryBuilder()
			.Register<CatalogProductReproCache>()
			.Register<PredefinedOfferBuilderReproCache>()
			.Build();
		_products = _registry.GetCache<CatalogProductReproCache>();
		_orders = _registry.GetCache<PredefinedOfferBuilderReproCache>();

		_products.AddOrUpdate(new CatalogProductRepro { EventId = 100, ChannelId = SalesChannel.Web, Name = "WebProduct" });
		_products.AddOrUpdate(new CatalogProductRepro { EventId = 100, ChannelId = SalesChannel.Store, Name = "StoreProduct" });
		_orders.AddOrUpdate(new PredefinedOfferBuilderRepro { Id = _b1, EventId = 100, Label = "b1" });
		_orders.AddOrUpdate(new PredefinedOfferBuilderRepro { Id = _b2, EventId = 100, Label = "b2" });
		_orders.AddOrUpdate(new PredefinedOfferBuilderRepro { Id = _b3, EventId = 200, Label = "b3" });
	}

	[Test]
	public void SelectorFkIndex_IsKeyedByRawFkType() {
		var indexType = _orders.EventIdIndex.GetType();
		Assert.That(indexType.Name, Does.StartWith("CacheSymmetricKeyValueListIndex"));
		Assert.That(indexType.GetGenericArguments()[^1], Is.EqualTo(typeof(long)));
	}

	[Test]
	public void WithEventId_StandaloneFilter_NoJoin() {
		using var results = _orders.Query().WithEventId(100L).ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var b in results)
			Assert.That(b.EventId, Is.EqualTo(100));
	}

	[Test]
	public void ForwardJoin_MatchesWebProductViaSelector() {
		using var results = _orders.Query().JoinWithCatalogProductRepro().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(3));
		foreach (var row in results) {
			if (row.Left.EventId == 100) {
				Assert.That(row.Right, Is.Not.Null);
				Assert.That(row.Right!.Name, Is.EqualTo("WebProduct"));
			}
			else {
				Assert.That(row.Right, Is.Null, "EventId 200 has no CatalogProduct");
			}
		}
	}

	[Test]
	public void WithEventId_ThenJoin_Composes() {
		using var results = _orders.Query().WithEventId(100L).JoinWithCatalogProductRepro().ExecutePooled();
		Assert.That(results.Count, Is.EqualTo(2));
		foreach (var row in results) {
			Assert.That(row.Left.EventId, Is.EqualTo(100));
			Assert.That(row.Right!.Name, Is.EqualTo("WebProduct"));
		}
	}
}

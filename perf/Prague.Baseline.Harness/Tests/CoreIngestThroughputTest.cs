namespace Prague.Baseline.Harness.Tests;

using Prague.Baseline.Scenario;
using Prague.Core;

/// <summary>
///   Core-only ingest throughput: loads the whole dataset into fresh generated caches via direct
///   <c>AddOrUpdate</c> loops (no Kafka), timing only the apply loops. Mirrors <c>CoreIngestBenchmarks.IngestAll</c>.
/// </summary>
public sealed class CoreIngestThroughputTest : IThroughputTest {
	public int RequiredProcessorCount => 1;

	public long Run(ThroughputSessionContext ctx) {
		var data = DatasetFactory.Build();
		var registry = new DataCacheRegistryBuilder()
			.Register<BaselineProductCache>().Register<BaselineProductInfoCache>()
			.Register<BaselineOfferCache>().Build();
		var products = registry.GetCache<BaselineProductCache>();
		var infos = registry.GetCache<BaselineProductInfoCache>();
		var offers = registry.GetCache<BaselineOfferCache>();

		ctx.Start();
		for (var i = 0; i < data.Products.Length; i++) products.AddOrUpdate(data.Products[i]);
		for (var i = 0; i < data.Infos.Length; i++) infos.AddOrUpdate(data.Infos[i]);
		for (var i = 0; i < data.Offers.Length; i++) offers.AddOrUpdate(data.Offers[i]);
		ctx.Stop();
		return ScenarioSpec.TotalEntities;
	}
}
